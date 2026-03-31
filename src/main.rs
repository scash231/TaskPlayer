#![windows_subsystem = "windows"]

use std::ffi::c_void;
use std::mem;
use std::sync::atomic::{AtomicIsize, Ordering};

use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::Graphics::Gdi::*;
use windows::Win32::System::Com::*;
use windows::Win32::System::LibraryLoader::*;
use windows::Win32::System::Registry::*;
use windows::Win32::System::Threading::*;
use windows::Win32::System::WindowsProgramming::*;
use windows::Win32::UI::Accessibility::*;
use windows::Win32::UI::Controls::*;
use windows::Win32::UI::Input::KeyboardAndMouse::*;
use windows::Win32::UI::Shell::*;
use windows::Win32::UI::WindowsAndMessaging::*;

use windows::Foundation::TypedEventHandler;
use windows::Media::Control::*;

const COLOR_KEY: COLORREF = COLORREF(0x00010101);
const WM_MEDIA_UPDATE: u32 = WM_USER + 1;
const WM_TRAYICON: u32 = WM_USER + 2;
const WM_DO_REPOSITION: u32 = WM_USER + 3;

const IDM_SETTINGS: u16 = 2001;
const IDM_EXIT: u16 = 2002;
const IDS_STARTUP: u16 = 3001;
const IDS_SHOWPLAYER: u16 = 3002;
const IDS_OK: u16 = 3003;
const IDS_CANCEL: u16 = 3004;
const IDS_TAB: u16 = 3005;
const IDS_TIMER_EDIT: u16 = 3010;
const IDS_BTNSZ_EDIT: u16 = 3020;
const IDS_BTNGAP_EDIT: u16 = 3021;
const IDS_HOVER_EDIT: u16 = 3022;
const IDS_PRESSED_EDIT: u16 = 3023;
const IDS_XOFFSET_EDIT: u16 = 3030;

const TIMER_REPOSITION: usize = 1;
const BTN_PREV: usize = 0;
const BTN_PLAY: usize = 1;
const BTN_NEXT: usize = 2;

const DEFAULT_BTN_SZ: i32 = 48;
const DEFAULT_BTN_GAP: i32 = 2;
const DEFAULT_TIMER_MS: u32 = 100;
const DEFAULT_HOVER_COLOR: u32 = 0x00464646;
const DEFAULT_PRESSED_COLOR: u32 = 0x00696969;
const DEFAULT_X_OFFSET: i32 = 0;

const TCM_INSERTITEMW: u32 = 0x133E;
const TCM_GETCURSEL: u32 = 0x130B;
const TCN_SELCHANGE: u32 = 0xFFFFFDD8;
const TCIF_TEXT: u32 = 0x0001;

static GLYPH_PREV: [u16; 2] = [0xE892, 0];
static GLYPH_PLAY: [u16; 2] = [0xE768, 0];
static GLYPH_PAUSE: [u16; 2] = [0xE769, 0];
static GLYPH_NEXT: [u16; 2] = [0xE893, 0];

static MAIN_HWND: AtomicIsize = AtomicIsize::new(0);
static TASKBAR_HWND: AtomicIsize = AtomicIsize::new(0);

#[repr(C)]
struct RawTcItem {
    mask: u32,
    state: u32,
    state_mask: u32,
    text: *mut u16,
    text_max: i32,
    image: i32,
    lparam: isize,
}

#[repr(C)]
struct RawNmHdr {
    _hwnd_from: isize,
    _id_from: usize,
    code: u32,
}

#[derive(Clone)]
struct UserSettings {
    show_player: bool,
    timer_ms: u32,
    btn_sz: i32,
    btn_gap: i32,
    hover_color: u32,
    pressed_color: u32,
    x_offset: i32,
}

impl Default for UserSettings {
    fn default() -> Self {
        Self {
            show_player: true,
            timer_ms: DEFAULT_TIMER_MS,
            btn_sz: DEFAULT_BTN_SZ,
            btn_gap: DEFAULT_BTN_GAP,
            hover_color: DEFAULT_HOVER_COLOR,
            pressed_color: DEFAULT_PRESSED_COLOR,
            x_offset: DEFAULT_X_OFFSET,
        }
    }
}

impl UserSettings {
    fn win_w(&self) -> i32 { self.btn_sz * 3 + self.btn_gap * 2 + 8 }
    fn win_h(&self) -> i32 { self.btn_sz }
}

struct AppState {
    hwnd: HWND,
    font_small: HFONT,
    font_large: HFONT,
    br_key: HBRUSH,
    br_hover: HBRUSH,
    br_pressed: HBRUSH,
    btn_rects: [RECT; 3],
    hover_idx: Option<usize>,
    pressed_idx: Option<usize>,
    playing: bool,
    player_visible: bool,
    tracking_mouse: bool,
    mgr: Option<GlobalSystemMediaTransportControlsSessionManager>,
    session: Option<GlobalSystemMediaTransportControlsSession>,
    tok_session: i64,
    tok_playback: i64,
    tok_props: i64,
    tray_icon: HICON,
    nid: NOTIFYICONDATAW,
    settings_hwnd: HWND,
    dpi: i32,
    hook: HWINEVENTHOOK,
    settings: UserSettings,
}

fn main() {
    unsafe { run(); }
}

unsafe fn run() {
    let _ = CoInitializeEx(None, COINIT_MULTITHREADED);

    let icex = INITCOMMONCONTROLSEX {
        dwSize: mem::size_of::<INITCOMMONCONTROLSEX>() as u32,
        dwICC: ICC_TAB_CLASSES,
    };
    let _ = InitCommonControlsEx(&icex);

    let hinstance = GetModuleHandleW(None).unwrap_or_default();

    let _mutex = CreateMutexW(None, true, w!("TaskbarMiniPlayer_Mutex"));
    if GetLastError() == ERROR_ALREADY_EXISTS {
        return;
    }

    let wc = WNDCLASSEXW {
        cbSize: mem::size_of::<WNDCLASSEXW>() as u32,
        lpfnWndProc: Some(wnd_proc),
        hInstance: hinstance.into(),
        hCursor: LoadCursorW(None, IDC_ARROW).unwrap_or_default(),
        hbrBackground: CreateSolidBrush(COLOR_KEY),
        lpszClassName: w!("TaskbarMiniPlayerClass"),
        ..mem::zeroed()
    };
    RegisterClassExW(&wc);

    let sc = WNDCLASSEXW {
        cbSize: mem::size_of::<WNDCLASSEXW>() as u32,
        lpfnWndProc: Some(settings_proc),
        hInstance: hinstance.into(),
        hCursor: LoadCursorW(None, IDC_ARROW).unwrap_or_default(),
        hbrBackground: HBRUSH((COLOR_WINDOW.0 + 1) as *mut c_void),
        lpszClassName: w!("TaskbarMiniPlayerSettings"),
        ..mem::zeroed()
    };
    RegisterClassExW(&sc);

    let settings = load_user_settings();

    let hwnd = CreateWindowExW(
        WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
        w!("TaskbarMiniPlayerClass"),
        w!("TaskbarMiniPlayer"),
        WS_POPUP,
        0, 0, settings.win_w(), settings.win_h(),
        None, None, Some(hinstance.into()), None,
    ).expect("CreateWindowExW failed");

    let _ = SetLayeredWindowAttributes(hwnd, COLOR_KEY, 0, LWA_COLORKEY);
    MAIN_HWND.store(hwnd.0 as isize, Ordering::Relaxed);

    let dpi = get_dpi();
    let scale = settings.btn_sz as f64 / 48.0;
    let font_small = CreateFontW(
        -MulDiv((18.0 * scale).round() as i32, dpi, 96), 0, 0, 0, FW_NORMAL.0 as i32,
        0, 0, 0, DEFAULT_CHARSET, FONT_OUTPUT_PRECISION(0), FONT_CLIP_PRECISION(0),
        ANTIALIASED_QUALITY, 0, w!("Segoe MDL2 Assets"),
    );
    let font_large = CreateFontW(
        -MulDiv((22.0 * scale).round() as i32, dpi, 96), 0, 0, 0, FW_NORMAL.0 as i32,
        0, 0, 0, DEFAULT_CHARSET, FONT_OUTPUT_PRECISION(0), FONT_CLIP_PRECISION(0),
        ANTIALIASED_QUALITY, 0, w!("Segoe MDL2 Assets"),
    );

    let br_key     = CreateSolidBrush(COLOR_KEY);
    let br_hover   = CreateSolidBrush(COLORREF(settings.hover_color));
    let br_pressed = CreateSolidBrush(COLORREF(settings.pressed_color));
    let player_visible = settings.show_player;
    let tray_icon = create_tray_icon_glyph();

    let hook = SetWinEventHook(
        EVENT_OBJECT_LOCATIONCHANGE,
        EVENT_OBJECT_LOCATIONCHANGE,
        None,
        Some(win_event_proc),
        0, 0,
        WINEVENT_OUTOFCONTEXT,
    );

    let timer_ms = settings.timer_ms;

    let mut state = Box::new(AppState {
        hwnd,
        font_small,
        font_large,
        br_key,
        br_hover,
        br_pressed,
        btn_rects: [mem::zeroed(); 3],
        hover_idx: None,
        pressed_idx: None,
        playing: false,
        player_visible,
        tracking_mouse: false,
        mgr: None,
        session: None,
        tok_session: 0,
        tok_playback: 0,
        tok_props: 0,
        tray_icon,
        nid: mem::zeroed(),
        settings_hwnd: HWND::default(),
        dpi,
        hook,
        settings,
    });

    embed_in_taskbar(hwnd, &state);
    compute_button_rects(&mut state);
    setup_tray_icon(&mut state);
    init_media(&mut state);

    let state_ptr = Box::into_raw(state);
    SetWindowLongPtrW(hwnd, GWLP_USERDATA, state_ptr as isize);

    if player_visible {
        let _ = ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        let _ = UpdateWindow(hwnd);
    }
    let _ = SetTimer(Some(hwnd), TIMER_REPOSITION, timer_ms, None);

    let mut msg: MSG = mem::zeroed();
    while GetMessageW(&mut msg, None, 0, 0).as_bool() {
        let st = &*state_ptr;
        if st.settings_hwnd != HWND::default()
            && IsWindow(Some(st.settings_hwnd)).as_bool()
            && IsDialogMessageW(st.settings_hwnd, &msg).as_bool()
        {
            continue;
        }
        let _ = TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    let st = Box::from_raw(state_ptr);
    remove_tray_icon(&st);
    let _ = DeleteObject(st.font_small.into());
    let _ = DeleteObject(st.font_large.into());
    let _ = DeleteObject(st.br_key.into());
    let _ = DeleteObject(st.br_hover.into());
    let _ = DeleteObject(st.br_pressed.into());
    if !st.tray_icon.is_invalid() {
        let _ = DestroyIcon(st.tray_icon);
    }
}

unsafe extern "system" fn win_event_proc(
    _hook: HWINEVENTHOOK,
    _event: u32,
    hwnd: HWND,
    id_object: i32,
    _id_child: i32,
    _thread: u32,
    _time: u32,
) {
    if id_object != 0 { return; }
    let taskbar = TASKBAR_HWND.load(Ordering::Relaxed);
    if taskbar == 0 || hwnd.0 as isize != taskbar { return; }
    let h = HWND(MAIN_HWND.load(Ordering::Relaxed) as *mut c_void);
    if h.0.is_null() { return; }
    let _ = PostMessageW(Some(h), WM_DO_REPOSITION, WPARAM(0), LPARAM(0));
}

unsafe fn get_dpi() -> i32 {
    let h = GetDC(None);
    let d = GetDeviceCaps(Some(h), LOGPIXELSX);
    ReleaseDC(None, h);
    d
}

unsafe fn embed_in_taskbar(hwnd: HWND, state: &AppState) {
    let taskbar = match FindWindowW(w!("Shell_TrayWnd"), None) {
        Ok(h) => h,
        Err(_) => return,
    };
    TASKBAR_HWND.store(taskbar.0 as isize, Ordering::Relaxed);

    let mut tb_rect: RECT = mem::zeroed();
    let _ = GetWindowRect(taskbar, &mut tb_rect);
    let tb_h = tb_rect.bottom - tb_rect.top;

    let dpi_scale = state.dpi as f64 / 96.0;
    let my_w = (state.settings.win_w() as f64 * dpi_scale) as i32;
    let my_h = tb_h.min((state.settings.win_h() as f64 * dpi_scale) as i32);
    let x_pos = calc_x_pos(taskbar, &tb_rect, my_w, dpi_scale) + state.settings.x_offset;
    let y_pos = tb_rect.top + (tb_h - my_h) / 2;
    let _ = SetWindowPos(hwnd, Some(HWND_TOPMOST),
        x_pos, y_pos, my_w, my_h,
        SWP_NOACTIVATE | SWP_SHOWWINDOW);
}

unsafe fn calc_x_pos(taskbar: HWND, tb_rect: &RECT, my_w: i32, dpi_scale: f64) -> i32 {
    if let Ok(tn) = FindWindowExW(Some(taskbar), None, w!("TrayNotifyWnd"), None) {
        let mut r: RECT = mem::zeroed();
        let _ = GetWindowRect(tn, &mut r);
        r.left - my_w - 4
    } else {
        tb_rect.right - my_w - (350.0 * dpi_scale) as i32
    }
}

unsafe fn reposition(state: &AppState) {
    let taskbar_raw = TASKBAR_HWND.load(Ordering::Relaxed);
    if taskbar_raw == 0 { return; }
    let taskbar = HWND(taskbar_raw as *mut c_void);

    let dpi_scale = state.dpi as f64 / 96.0;
    let my_w = (state.settings.win_w() as f64 * dpi_scale) as i32;
    let mut tb_rect: RECT = mem::zeroed();
    let _ = GetWindowRect(taskbar, &mut tb_rect);
    let tb_h = tb_rect.bottom - tb_rect.top;
    let my_h = tb_h.min((state.settings.win_h() as f64 * dpi_scale) as i32);
    let x_pos = calc_x_pos(taskbar, &tb_rect, my_w, dpi_scale) + state.settings.x_offset;
    let y_pos = tb_rect.top + (tb_h - my_h) / 2;
    let _ = SetWindowPos(state.hwnd, Some(HWND_TOPMOST),
        x_pos, y_pos, my_w, my_h, SWP_NOACTIVATE);
}

unsafe fn compute_button_rects(state: &mut AppState) {
    let mut rc: RECT = mem::zeroed();
    let _ = GetClientRect(state.hwnd, &mut rc);
    let cw = rc.right;
    let ch = rc.bottom;
    let btn_sz = state.settings.btn_sz;
    let btn_gap = state.settings.btn_gap;
    let total_w = btn_sz * 3 + btn_gap * 2;
    let x0 = (cw - total_w) / 2;
    let y0 = (ch - btn_sz) / 2;
    for i in 0..3usize {
        state.btn_rects[i] = RECT {
            left: x0 + (btn_sz + btn_gap) * i as i32,
            top: y0,
            right: x0 + (btn_sz + btn_gap) * i as i32 + btn_sz,
            bottom: y0 + btn_sz,
        };
    }
}

fn hit_test_button(state: &AppState, x: i32, y: i32) -> Option<usize> {
    for (i, r) in state.btn_rects.iter().enumerate() {
        if x >= r.left && x < r.right && y >= r.top && y < r.bottom {
            return Some(i);
        }
    }
    None
}

unsafe fn paint(state: &AppState) {
    let mut ps: PAINTSTRUCT = mem::zeroed();
    let hdc = BeginPaint(state.hwnd, &mut ps);
    let mut rc: RECT = mem::zeroed();
    let _ = GetClientRect(state.hwnd, &mut rc);
    FillRect(hdc, &rc, state.br_key);

    for i in 0..3usize {
        let br = &state.btn_rects[i];
        let hovered = state.hover_idx == Some(i);
        let pressed = hovered && state.pressed_idx == Some(i);

        if pressed {
            FillRect(hdc, br, state.br_pressed);
        } else if hovered {
            FillRect(hdc, br, state.br_hover);
        }

        let (glyph, font): (&[u16], HFONT) = match i {
            BTN_PREV => (&GLYPH_PREV, state.font_small),
            BTN_PLAY => {
                if state.playing { (&GLYPH_PAUSE, state.font_large) }
                else { (&GLYPH_PLAY, state.font_large) }
            }
            BTN_NEXT => (&GLYPH_NEXT, state.font_small),
            _ => continue,
        };

        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, COLORREF(0x00FFFFFF));
        let old = SelectObject(hdc, font.into());
        let mut tr = *br;
        let mut buf: Vec<u16> = glyph.to_vec();
        DrawTextW(hdc, &mut buf, &mut tr, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        SelectObject(hdc, old);
    }
    let _ = EndPaint(state.hwnd, &ps);
}

unsafe fn create_tray_icon_glyph() -> HICON {
    let sz = GetSystemMetrics(SM_CXSMICON);
    let hs = GetDC(None);
    let hdc = CreateCompatibleDC(Some(hs));
    let hbc = CreateCompatibleBitmap(hs, sz, sz);
    let hbm = CreateBitmap(sz, sz, 1, 1, None);
    SelectObject(hdc, hbc.into());
    let rc = RECT { left: 0, top: 0, right: sz, bottom: sz };
    let b = CreateSolidBrush(COLORREF(0x001E1E1E));
    FillRect(hdc, &rc, b);
    let _ = DeleteObject(b.into());
    let hf = CreateFontW(
        -(sz - 4), 0, 0, 0, FW_NORMAL.0 as i32,
        0, 0, 0, DEFAULT_CHARSET, FONT_OUTPUT_PRECISION(0), FONT_CLIP_PRECISION(0),
        ANTIALIASED_QUALITY, 0, w!("Segoe MDL2 Assets"),
    );
    SelectObject(hdc, hf.into());
    SetBkMode(hdc, TRANSPARENT);
    SetTextColor(hdc, COLORREF(0x00FFFFFF));
    let mut rm = rc;
    let mut buf = GLYPH_PLAY.to_vec();
    DrawTextW(hdc, &mut buf, &mut rm, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    let _ = DeleteObject(hf.into());
    let ii = ICONINFO {
        fIcon: TRUE,
        hbmMask: hbm,
        hbmColor: hbc,
        ..mem::zeroed()
    };
    let icon = CreateIconIndirect(&ii).unwrap_or_default();
    let _ = DeleteObject(hbc.into());
    let _ = DeleteObject(hbm.into());
    let _ = DeleteDC(hdc);
    ReleaseDC(None, hs);
    icon
}

unsafe fn setup_tray_icon(state: &mut AppState) {
    state.nid = mem::zeroed();
    state.nid.cbSize = mem::size_of::<NOTIFYICONDATAW>() as u32;
    state.nid.hWnd = state.hwnd;
    state.nid.uID = 1;
    state.nid.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    state.nid.uCallbackMessage = WM_TRAYICON;
    state.nid.hIcon = state.tray_icon;
    for (i, ch) in "TaskbarMiniPlayer".encode_utf16().enumerate() {
        if i >= 127 { break; }
        state.nid.szTip[i] = ch;
    }
    let _ = Shell_NotifyIconW(NIM_ADD, &state.nid);
}

unsafe fn remove_tray_icon(state: &AppState) {
    let _ = Shell_NotifyIconW(NIM_DELETE, &state.nid);
}

unsafe fn show_tray_context_menu(state: &AppState) {
    let hm = CreatePopupMenu().unwrap();
    let _ = AppendMenuW(hm, MF_STRING, IDM_SETTINGS as usize, w!("Settings..."));
    let _ = AppendMenuW(hm, MF_SEPARATOR, 0, None);
    let _ = AppendMenuW(hm, MF_STRING, IDM_EXIT as usize, w!("Exit"));
    let mut pt: POINT = mem::zeroed();
    let _ = GetCursorPos(&mut pt);
    let _ = SetForegroundWindow(state.hwnd);
    let _ = TrackPopupMenu(hm, TPM_RIGHTBUTTON, pt.x, pt.y, None, state.hwnd, None);
    let _ = DestroyMenu(hm);
}

fn colorref_to_rrggbb(cr: u32) -> u32 {
    let r = cr & 0xFF;
    let g = (cr >> 8) & 0xFF;
    let b = (cr >> 16) & 0xFF;
    (r << 16) | (g << 8) | b
}

fn rrggbb_to_colorref(rgb: u32) -> u32 {
    let r = (rgb >> 16) & 0xFF;
    let g = (rgb >> 8) & 0xFF;
    let b = rgb & 0xFF;
    (b << 16) | (g << 8) | r
}

unsafe fn get_edit_num(hwnd: HWND) -> i32 {
    let mut buf = [0u16; 32];
    let len = GetWindowTextW(hwnd, &mut buf) as usize;
    String::from_utf16_lossy(&buf[..len]).trim().parse().unwrap_or(0)
}

unsafe fn get_edit_hex(hwnd: HWND) -> u32 {
    let mut buf = [0u16; 32];
    let len = GetWindowTextW(hwnd, &mut buf) as usize;
    let s = String::from_utf16_lossy(&buf[..len]);
    u32::from_str_radix(s.trim().trim_start_matches('#'), 16).unwrap_or(0)
}

unsafe fn set_edit_num(hwnd: HWND, val: i32) {
    let s: Vec<u16> = format!("{}", val).encode_utf16().chain(std::iter::once(0)).collect();
    let _ = SetWindowTextW(hwnd, PCWSTR(s.as_ptr()));
}

unsafe fn set_edit_hex_color(hwnd: HWND, colorref: u32) {
    let rgb = colorref_to_rrggbb(colorref);
    let s: Vec<u16> = format!("{:06X}", rgb).encode_utf16().chain(std::iter::once(0)).collect();
    let _ = SetWindowTextW(hwnd, PCWSTR(s.as_ptr()));
}

unsafe fn insert_tab(tab_ctrl: HWND, index: usize, title: &str) {
    let mut text: Vec<u16> = title.encode_utf16().chain(std::iter::once(0)).collect();
    let item = RawTcItem {
        mask: TCIF_TEXT,
        state: 0,
        state_mask: 0,
        text: text.as_mut_ptr(),
        text_max: 0,
        image: -1,
        lparam: 0,
    };
    SendMessageW(tab_ctrl, TCM_INSERTITEMW,
        Some(WPARAM(index)),
        Some(LPARAM(&item as *const _ as isize)));
}

unsafe fn switch_tab(sd: &SettingsData, tab: i32) {
    let show_hide = |ctrls: &[HWND], visible: bool| {
        for &c in ctrls {
            let _ = ShowWindow(c, if visible { SW_SHOW } else { SW_HIDE });
        }
    };
    show_hide(&sd.general_ctrls, tab == 0);
    show_hide(&sd.appearance_ctrls, tab == 1);
    show_hide(&sd.position_ctrls, tab == 2);
}

struct SettingsData {
    parent_hwnd: HWND,
    tab_ctrl: HWND,
    chk_startup: HWND,
    chk_show: HWND,
    edt_timer: HWND,
    edt_btn_sz: HWND,
    edt_btn_gap: HWND,
    edt_hover: HWND,
    edt_pressed: HWND,
    edt_xoffset: HWND,
    general_ctrls: Vec<HWND>,
    appearance_ctrls: Vec<HWND>,
    position_ctrls: Vec<HWND>,
}

unsafe fn show_settings_window(state: &mut AppState) {
    if state.settings_hwnd != HWND::default()
        && IsWindow(Some(state.settings_hwnd)).as_bool()
    {
        let _ = SetForegroundWindow(state.settings_hwnd);
        return;
    }
    let hi = GetModuleHandleW(None).unwrap_or_default();
    let sw = 420i32;
    let sh = 360i32;
    let sx = (GetSystemMetrics(SM_CXSCREEN) - sw) / 2;
    let sy = (GetSystemMetrics(SM_CYSCREEN) - sh) / 2;
    let shwnd = CreateWindowExW(
        WS_EX_DLGMODALFRAME | WS_EX_TOPMOST,
        w!("TaskbarMiniPlayerSettings"),
        w!("TaskbarMiniPlayer \u{2013} Settings"),
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU,
        sx, sy, sw, sh,
        None, None, Some(hi.into()), None,
    ).expect("Settings window failed");
    state.settings_hwnd = shwnd;

    let font: HFONT = HFONT(GetStockObject(DEFAULT_GUI_FONT).0);

    let mk_ctrl = |cls: PCWSTR, txt: PCWSTR, sty: WINDOW_STYLE,
              id: u16, x: i32, y: i32, cw: i32, ch: i32| -> HWND {
        let c = CreateWindowExW(
            WINDOW_EX_STYLE(0), cls, txt, sty,
            x, y, cw, ch,
            Some(shwnd), Some(HMENU(id as *mut c_void)),
            Some(hi.into()), None,
        ).unwrap();
        SendMessageW(c, WM_SETFONT, Some(WPARAM(font.0 as usize)), Some(LPARAM(1)));
        c
    };

    let tab_ctrl = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("SysTabControl32"),
        w!(""),
        WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
        8, 8, 396, 250,
        Some(shwnd), Some(HMENU(IDS_TAB as *mut c_void)),
        Some(hi.into()), None,
    ).unwrap();
    SendMessageW(tab_ctrl, WM_SETFONT, Some(WPARAM(font.0 as usize)), Some(LPARAM(1)));
    insert_tab(tab_ctrl, 0, "General");
    insert_tab(tab_ctrl, 1, "Appearance");
    insert_tab(tab_ctrl, 2, "Position");

    let s = &state.settings;
    let mut general_ctrls: Vec<HWND> = Vec::new();
    let mut appearance_ctrls: Vec<HWND> = Vec::new();
    let mut position_ctrls: Vec<HWND> = Vec::new();

    let c = mk_ctrl(w!("BUTTON"), w!("Launch on Windows startup"),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | WINDOW_STYLE(BS_AUTOCHECKBOX as u32),
        IDS_STARTUP, 24, 42, 360, 22);
    if is_startup_enabled() {
        SendMessageW(c, BM_SETCHECK, Some(WPARAM(BST_CHECKED.0 as usize)), Some(LPARAM(0)));
    }
    let chk_startup = c;
    general_ctrls.push(c);

    let c = mk_ctrl(w!("BUTTON"), w!("Show player controls in taskbar"),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | WINDOW_STYLE(BS_AUTOCHECKBOX as u32),
        IDS_SHOWPLAYER, 24, 70, 360, 22);
    if s.show_player {
        SendMessageW(c, BM_SETCHECK, Some(WPARAM(BST_CHECKED.0 as usize)), Some(LPARAM(0)));
    }
    let chk_show = c;
    general_ctrls.push(c);

    let c = mk_ctrl(w!("STATIC"), w!("Timer interval (ms):"),
        WS_CHILD | WS_VISIBLE, 0, 24, 104, 170, 20);
    general_ctrls.push(c);

    let edt_timer = mk_ctrl(w!("EDIT"), w!(""),
        WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_BORDER,
        IDS_TIMER_EDIT, 200, 102, 80, 22);
    set_edit_num(edt_timer, s.timer_ms as i32);
    general_ctrls.push(edt_timer);

    let c = mk_ctrl(w!("STATIC"), w!("(10 \u{2013} 5000)"),
        WS_CHILD | WS_VISIBLE, 0, 286, 104, 100, 20);
    general_ctrls.push(c);

    let c = mk_ctrl(w!("STATIC"), w!("Button size (px):"),
        WS_CHILD, 0, 24, 42, 170, 20);
    appearance_ctrls.push(c);

    let edt_btn_sz = mk_ctrl(w!("EDIT"), w!(""),
        WS_CHILD | WS_TABSTOP | WS_BORDER,
        IDS_BTNSZ_EDIT, 200, 40, 80, 22);
    set_edit_num(edt_btn_sz, s.btn_sz);
    appearance_ctrls.push(edt_btn_sz);

    let c = mk_ctrl(w!("STATIC"), w!("(24 \u{2013} 72)"),
        WS_CHILD, 0, 286, 42, 100, 20);
    appearance_ctrls.push(c);

    let c = mk_ctrl(w!("STATIC"), w!("Button spacing (px):"),
        WS_CHILD, 0, 24, 72, 170, 20);
    appearance_ctrls.push(c);

    let edt_btn_gap = mk_ctrl(w!("EDIT"), w!(""),
        WS_CHILD | WS_TABSTOP | WS_BORDER,
        IDS_BTNGAP_EDIT, 200, 70, 80, 22);
    set_edit_num(edt_btn_gap, s.btn_gap);
    appearance_ctrls.push(edt_btn_gap);

    let c = mk_ctrl(w!("STATIC"), w!("(0 \u{2013} 20)"),
        WS_CHILD, 0, 286, 72, 100, 20);
    appearance_ctrls.push(c);

    let c = mk_ctrl(w!("STATIC"), w!("Hover color (RRGGBB):"),
        WS_CHILD, 0, 24, 106, 170, 20);
    appearance_ctrls.push(c);

    let edt_hover = mk_ctrl(w!("EDIT"), w!(""),
        WS_CHILD | WS_TABSTOP | WS_BORDER,
        IDS_HOVER_EDIT, 200, 104, 80, 22);
    set_edit_hex_color(edt_hover, s.hover_color);
    appearance_ctrls.push(edt_hover);

    let c = mk_ctrl(w!("STATIC"), w!("Pressed color (RRGGBB):"),
        WS_CHILD, 0, 24, 136, 170, 20);
    appearance_ctrls.push(c);

    let edt_pressed = mk_ctrl(w!("EDIT"), w!(""),
        WS_CHILD | WS_TABSTOP | WS_BORDER,
        IDS_PRESSED_EDIT, 200, 134, 80, 22);
    set_edit_hex_color(edt_pressed, s.pressed_color);
    appearance_ctrls.push(edt_pressed);

    let c = mk_ctrl(w!("STATIC"), w!("X offset from default (px):"),
        WS_CHILD, 0, 24, 42, 200, 20);
    position_ctrls.push(c);

    let edt_xoffset = mk_ctrl(w!("EDIT"), w!(""),
        WS_CHILD | WS_TABSTOP | WS_BORDER,
        IDS_XOFFSET_EDIT, 230, 40, 80, 22);
    set_edit_num(edt_xoffset, s.x_offset);
    position_ctrls.push(edt_xoffset);

    let c = mk_ctrl(w!("STATIC"), w!("Negative = left, Positive = right"),
        WS_CHILD, 0, 24, 68, 280, 20);
    position_ctrls.push(c);

    mk_ctrl(w!("BUTTON"), w!("OK"),
       WS_CHILD | WS_VISIBLE | WS_TABSTOP | WINDOW_STYLE(BS_DEFPUSHBUTTON as u32),
       IDS_OK, 220, 280, 80, 28);
    mk_ctrl(w!("BUTTON"), w!("Cancel"),
       WS_CHILD | WS_VISIBLE | WS_TABSTOP | WINDOW_STYLE(BS_PUSHBUTTON as u32),
       IDS_CANCEL, 310, 280, 80, 28);

    let sd = Box::new(SettingsData {
        parent_hwnd: state.hwnd,
        tab_ctrl,
        chk_startup,
        chk_show,
        edt_timer,
        edt_btn_sz,
        edt_btn_gap,
        edt_hover,
        edt_pressed,
        edt_xoffset,
        general_ctrls,
        appearance_ctrls,
        position_ctrls,
    });
    SetWindowLongPtrW(shwnd, GWLP_USERDATA, Box::into_raw(sd) as isize);
    let _ = ShowWindow(shwnd, SW_SHOW);
    let _ = SetForegroundWindow(shwnd);
}

unsafe extern "system" fn settings_proc(
    hwnd: HWND, msg: u32, wp: WPARAM, lp: LPARAM,
) -> LRESULT {
    match msg {
        WM_NOTIFY => {
            let p = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut SettingsData;
            if !p.is_null() {
                let sd = &*p;
                let nmhdr = &*(lp.0 as *const RawNmHdr);
                if nmhdr.code == TCN_SELCHANGE {
                    let sel = SendMessageW(sd.tab_ctrl, TCM_GETCURSEL,
                        None, None).0 as i32;
                    if sel >= 0 { switch_tab(sd, sel); }
                }
            }
            LRESULT(0)
        }
        WM_COMMAND => {
            let id = (wp.0 & 0xFFFF) as u16;
            let p = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut SettingsData;
            if p.is_null() {
                return DefWindowProcW(hwnd, msg, wp, lp);
            }
            let sd = &*p;
            match id {
                IDS_OK => {
                    let startup = SendMessageW(sd.chk_startup, BM_GETCHECK,
                        Some(WPARAM(0)), Some(LPARAM(0))) == LRESULT(BST_CHECKED.0 as isize);
                    let show = SendMessageW(sd.chk_show, BM_GETCHECK,
                        Some(WPARAM(0)), Some(LPARAM(0))) == LRESULT(BST_CHECKED.0 as isize);

                    let timer_ms = (get_edit_num(sd.edt_timer) as u32).clamp(10, 5000);
                    let btn_sz   = get_edit_num(sd.edt_btn_sz).clamp(24, 72);
                    let btn_gap  = get_edit_num(sd.edt_btn_gap).clamp(0, 20);
                    let hover_color   = rrggbb_to_colorref(get_edit_hex(sd.edt_hover));
                    let pressed_color = rrggbb_to_colorref(get_edit_hex(sd.edt_pressed));
                    let x_offset = get_edit_num(sd.edt_xoffset);

                    set_startup_enabled(startup);

                    let new_settings = UserSettings {
                        show_player: show,
                        timer_ms,
                        btn_sz,
                        btn_gap,
                        hover_color,
                        pressed_color,
                        x_offset,
                    };
                    save_user_settings(&new_settings);

                    let pp = GetWindowLongPtrW(sd.parent_hwnd, GWLP_USERDATA)
                        as *mut AppState;
                    if !pp.is_null() {
                        (*pp).settings = new_settings;
                        apply_settings(&mut *pp);
                    }
                    let _ = DestroyWindow(hwnd);
                    LRESULT(0)
                }
                IDS_CANCEL => {
                    let _ = DestroyWindow(hwnd);
                    LRESULT(0)
                }
                _ => DefWindowProcW(hwnd, msg, wp, lp),
            }
        }
        WM_DESTROY => {
            let p = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut SettingsData;
            if !p.is_null() {
                let _ = Box::from_raw(p);
                SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0);
            }
            let m = HWND(MAIN_HWND.load(Ordering::Relaxed) as *mut c_void);
            let pp = GetWindowLongPtrW(m, GWLP_USERDATA) as *mut AppState;
            if !pp.is_null() { (*pp).settings_hwnd = HWND::default(); }
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wp, lp),
    }
}

unsafe extern "system" fn wnd_proc(
    hwnd: HWND, msg: u32, wp: WPARAM, lp: LPARAM,
) -> LRESULT {
    let sp = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut AppState;
    if sp.is_null() {
        return DefWindowProcW(hwnd, msg, wp, lp);
    }
    let state = &mut *sp;
    match msg {
        WM_PAINT => { paint(state); LRESULT(0) }

        WM_MOUSEMOVE => {
            let x = (lp.0 & 0xFFFF) as i16 as i32;
            let y = ((lp.0 >> 16) & 0xFFFF) as i16 as i32;
            if !state.tracking_mouse {
                let mut tme = TRACKMOUSEEVENT {
                    cbSize: mem::size_of::<TRACKMOUSEEVENT>() as u32,
                    dwFlags: TME_LEAVE,
                    hwndTrack: hwnd,
                    dwHoverTime: 0,
                };
                let _ = TrackMouseEvent(&mut tme);
                state.tracking_mouse = true;
            }
            let nh = hit_test_button(state, x, y);
            if nh != state.hover_idx {
                state.hover_idx = nh;
                let _ = InvalidateRect(Some(hwnd), None, false);
            }
            LRESULT(0)
        }

        WM_MOUSELEAVE => {
            state.tracking_mouse = false;
            if state.hover_idx.is_some() {
                state.hover_idx = None;
                state.pressed_idx = None;
                let _ = InvalidateRect(Some(hwnd), None, false);
            }
            LRESULT(0)
        }

        WM_LBUTTONDOWN => {
            let x = (lp.0 & 0xFFFF) as i16 as i32;
            let y = ((lp.0 >> 16) & 0xFFFF) as i16 as i32;
            state.pressed_idx = hit_test_button(state, x, y);
            if state.pressed_idx.is_some() {
                SetCapture(hwnd);
                let _ = InvalidateRect(Some(hwnd), None, false);
            }
            LRESULT(0)
        }

        WM_LBUTTONUP => {
            let x = (lp.0 & 0xFFFF) as i16 as i32;
            let y = ((lp.0 >> 16) & 0xFFFF) as i16 as i32;
            let _ = ReleaseCapture();
            if let Some(pr) = state.pressed_idx.take() {
                if hit_test_button(state, x, y) == Some(pr) {
                    handle_button_click(state, pr);
                }
                let _ = InvalidateRect(Some(hwnd), None, false);
            }
            LRESULT(0)
        }

        WM_TIMER => {
            if wp.0 == TIMER_REPOSITION { reposition(state); }
            LRESULT(0)
        }

        WM_DO_REPOSITION => {
            reposition(state);
            LRESULT(0)
        }

        WM_TRAYICON => {
            let mm = (lp.0 & 0xFFFF) as u32;
            if mm == WM_RBUTTONUP || mm == WM_CONTEXTMENU {
                show_tray_context_menu(state);
            } else if mm == WM_LBUTTONDBLCLK {
                show_settings_window(state);
            }
            LRESULT(0)
        }

        WM_COMMAND => {
            match (wp.0 & 0xFFFF) as u16 {
                IDM_SETTINGS => show_settings_window(state),
                IDM_EXIT => { let _ = DestroyWindow(hwnd); }
                _ => {}
            }
            LRESULT(0)
        }

        WM_MEDIA_UPDATE => {
            if wp.0 == 1 {
                unbind_session(state);
                state.session = state.mgr.as_ref()
                    .and_then(|m| m.GetCurrentSession().ok());
                bind_session(state);
            } else {
                refresh_play_state(state);
            }
            LRESULT(0)
        }

        WM_DESTROY => {
            let _ = KillTimer(Some(hwnd), TIMER_REPOSITION);
            if !state.hook.is_invalid() {
                let _ = UnhookWinEvent(state.hook);
            }
            unbind_session(state);
            if let Some(mgr) = state.mgr.take() {
                let _ = mgr.RemoveCurrentSessionChanged(state.tok_session);
            }
            PostQuitMessage(0);
            LRESULT(0)
        }

        _ => DefWindowProcW(hwnd, msg, wp, lp),
    }
}

fn handle_button_click(state: &mut AppState, btn: usize) {
    let s = match &state.session {
        Some(s) => s,
        None => return,
    };
    match btn {
        BTN_PREV => { let _ = s.TrySkipPreviousAsync(); }
        BTN_PLAY => {
            if state.playing { let _ = s.TryPauseAsync(); }
            else { let _ = s.TryPlayAsync(); }
        }
        BTN_NEXT => { let _ = s.TrySkipNextAsync(); }
        _ => {}
    }
}

unsafe fn refresh_play_state(state: &mut AppState) {
    if let Some(session) = &state.session {
        if let Ok(info) = session.GetPlaybackInfo() {
            if let Ok(status) = info.PlaybackStatus() {
                let was = state.playing;
                state.playing = status
                    == GlobalSystemMediaTransportControlsSessionPlaybackStatus::Playing;
                if was != state.playing {
                    let _ = InvalidateRect(Some(state.hwnd), None, false);
                }
            }
        }
    }
}

unsafe fn bind_session(state: &mut AppState) {
    if let Some(session) = &state.session {
        if let Ok(tok) = session.PlaybackInfoChanged(&TypedEventHandler::new(
            move |_, _| {
                let h = HWND(MAIN_HWND.load(Ordering::Relaxed) as *mut c_void);
                PostMessageW(Some(h), WM_MEDIA_UPDATE, WPARAM(0), LPARAM(0))
            },
        )) {
            state.tok_playback = tok;
        }
        if let Ok(tok) = session.MediaPropertiesChanged(&TypedEventHandler::new(
            move |_, _| {
                let h = HWND(MAIN_HWND.load(Ordering::Relaxed) as *mut c_void);
                PostMessageW(Some(h), WM_MEDIA_UPDATE, WPARAM(0), LPARAM(0))
            },
        )) {
            state.tok_props = tok;
        }
        refresh_play_state(state);
    }
}

unsafe fn unbind_session(state: &mut AppState) {
    if let Some(session) = state.session.take() {
        let _ = session.RemovePlaybackInfoChanged(state.tok_playback);
        let _ = session.RemoveMediaPropertiesChanged(state.tok_props);
    }
}

unsafe fn init_media(state: &mut AppState) {
    let mgr = match GlobalSystemMediaTransportControlsSessionManager::RequestAsync() {
        Ok(op) => match op.get() {
            Ok(m) => m,
            Err(_) => return,
        },
        Err(_) => return,
    };
    state.session = mgr.GetCurrentSession().ok();
    if let Ok(t) = mgr.CurrentSessionChanged(&TypedEventHandler::new(
        move |_, _| {
            let h = HWND(MAIN_HWND.load(Ordering::Relaxed) as *mut c_void);
            PostMessageW(Some(h), WM_MEDIA_UPDATE, WPARAM(1), LPARAM(0))
        },
    )) {
        state.tok_session = t;
    }
    state.mgr = Some(mgr);
    bind_session(state);
}

unsafe fn is_startup_enabled() -> bool {
    let mut hk = HKEY::default();
    if RegOpenKeyExW(HKEY_CURRENT_USER,
        w!("Software\\Microsoft\\Windows\\CurrentVersion\\Run"),
        None, KEY_READ, &mut hk) != WIN32_ERROR(0)
    {
        return false;
    }
    let ok = RegQueryValueExW(hk, w!("TaskbarMiniPlayer"),
        None, None, None, None) == WIN32_ERROR(0);
    let _ = RegCloseKey(hk);
    ok
}

unsafe fn set_startup_enabled(enable: bool) {
    let mut hk = HKEY::default();
    if RegOpenKeyExW(HKEY_CURRENT_USER,
        w!("Software\\Microsoft\\Windows\\CurrentVersion\\Run"),
        None, KEY_SET_VALUE, &mut hk) != WIN32_ERROR(0)
    {
        return;
    }
    if enable {
        let mut path = [0u16; MAX_PATH as usize];
        let len = GetModuleFileNameW(None, &mut path) as usize;
        let bytes: &[u8] = std::slice::from_raw_parts(
            path.as_ptr() as *const u8, (len + 1) * 2);
        let _ = RegSetValueExW(hk, w!("TaskbarMiniPlayer"),
            None, REG_SZ, Some(bytes));
    } else {
        let _ = RegDeleteValueW(hk, w!("TaskbarMiniPlayer"));
    }
    let _ = RegCloseKey(hk);
}

unsafe fn read_reg_u32(hk: HKEY, name: PCWSTR, default: u32) -> u32 {
    let mut val: u32 = default;
    let mut sz = mem::size_of::<u32>() as u32;
    let _ = RegQueryValueExW(hk, name, None, None,
        Some(&mut val as *mut u32 as *mut u8), Some(&mut sz));
    val
}

unsafe fn write_reg_u32(hk: HKEY, name: PCWSTR, val: u32) {
    let bytes: &[u8] = std::slice::from_raw_parts(
        &val as *const u32 as *const u8, mem::size_of::<u32>());
    let _ = RegSetValueExW(hk, name, None, REG_DWORD, Some(bytes));
}

unsafe fn load_user_settings() -> UserSettings {
    let mut hk = HKEY::default();
    if RegOpenKeyExW(HKEY_CURRENT_USER,
        w!("Software\\TaskbarMiniPlayer"),
        None, KEY_READ, &mut hk) != WIN32_ERROR(0)
    {
        return UserSettings::default();
    }
    let mut s = UserSettings::default();
    s.show_player   = read_reg_u32(hk, w!("ShowPlayer"), 1) != 0;
    s.timer_ms      = read_reg_u32(hk, w!("TimerMs"), DEFAULT_TIMER_MS).clamp(10, 5000);
    s.btn_sz        = (read_reg_u32(hk, w!("ButtonSize"), DEFAULT_BTN_SZ as u32) as i32).clamp(24, 72);
    s.btn_gap       = (read_reg_u32(hk, w!("ButtonGap"), DEFAULT_BTN_GAP as u32) as i32).clamp(0, 20);
    s.hover_color   = read_reg_u32(hk, w!("HoverColor"), DEFAULT_HOVER_COLOR);
    s.pressed_color = read_reg_u32(hk, w!("PressedColor"), DEFAULT_PRESSED_COLOR);
    s.x_offset      = read_reg_u32(hk, w!("XOffset"), DEFAULT_X_OFFSET as u32) as i32;
    let _ = RegCloseKey(hk);
    s
}

unsafe fn save_user_settings(s: &UserSettings) {
    let mut hk = HKEY::default();
    let _ = RegCreateKeyExW(HKEY_CURRENT_USER,
        w!("Software\\TaskbarMiniPlayer"),
        None, None, REG_OPTION_NON_VOLATILE,
        KEY_SET_VALUE, None, &mut hk, None);
    write_reg_u32(hk, w!("ShowPlayer"), if s.show_player { 1 } else { 0 });
    write_reg_u32(hk, w!("TimerMs"), s.timer_ms);
    write_reg_u32(hk, w!("ButtonSize"), s.btn_sz as u32);
    write_reg_u32(hk, w!("ButtonGap"), s.btn_gap as u32);
    write_reg_u32(hk, w!("HoverColor"), s.hover_color);
    write_reg_u32(hk, w!("PressedColor"), s.pressed_color);
    write_reg_u32(hk, w!("XOffset"), s.x_offset as u32);
    let _ = RegCloseKey(hk);
}

unsafe fn apply_settings(state: &mut AppState) {
    let _ = DeleteObject(state.br_hover.into());
    let _ = DeleteObject(state.br_pressed.into());
    state.br_hover   = CreateSolidBrush(COLORREF(state.settings.hover_color));
    state.br_pressed = CreateSolidBrush(COLORREF(state.settings.pressed_color));

    let _ = DeleteObject(state.font_small.into());
    let _ = DeleteObject(state.font_large.into());
    let scale = state.settings.btn_sz as f64 / 48.0;
    state.font_small = CreateFontW(
        -MulDiv((18.0 * scale).round() as i32, state.dpi, 96),
        0, 0, 0, FW_NORMAL.0 as i32,
        0, 0, 0, DEFAULT_CHARSET, FONT_OUTPUT_PRECISION(0), FONT_CLIP_PRECISION(0),
        ANTIALIASED_QUALITY, 0, w!("Segoe MDL2 Assets"),
    );
    state.font_large = CreateFontW(
        -MulDiv((22.0 * scale).round() as i32, state.dpi, 96),
        0, 0, 0, FW_NORMAL.0 as i32,
        0, 0, 0, DEFAULT_CHARSET, FONT_OUTPUT_PRECISION(0), FONT_CLIP_PRECISION(0),
        ANTIALIASED_QUALITY, 0, w!("Segoe MDL2 Assets"),
    );

    let _ = KillTimer(Some(state.hwnd), TIMER_REPOSITION);
    let _ = SetTimer(Some(state.hwnd), TIMER_REPOSITION, state.settings.timer_ms, None);

    state.player_visible = state.settings.show_player;
    if state.player_visible {
        let _ = ShowWindow(state.hwnd, SW_SHOWNOACTIVATE);
    } else {
        let _ = ShowWindow(state.hwnd, SW_HIDE);
    }

    reposition(state);
    compute_button_rects(state);
    let _ = InvalidateRect(Some(state.hwnd), None, true);
}