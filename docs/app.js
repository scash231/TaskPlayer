function adjustLayout() {
    const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent) || window.innerWidth < 768;
    const body = document.body;
    const downloadSection = document.querySelector('.downloads');
    
    if (isMobile) {
        body.classList.add('phone');
        body.classList.remove('desktop');
        
        let notice = document.getElementById('mobile-notice');
        if (!notice && downloadSection) {
            notice = document.createElement('p');
            notice.id = 'mobile-notice';
            notice.style.fontSize = '11px';
            notice.style.color = '#444';
            notice.style.marginTop = '12px';
            notice.style.fontFamily = 'monospace';
            notice.innerText = 'Note: This application requires a Windows desktop environment.';
            downloadSection.appendChild(notice);
        }
    } else {
        body.classList.add('desktop');
        body.classList.remove('phone');
        
        const notice = document.getElementById('mobile-notice');
        if (notice) {
            notice.remove();
        }
    }
}

window.addEventListener('DOMContentLoaded', adjustLayout);
window.addEventListener('resize', adjustLayout);
