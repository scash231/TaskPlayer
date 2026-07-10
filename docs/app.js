let manualOverride = null;

function adjustLayout() {
    const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent) || window.innerWidth < 768;
    const currentMode = manualOverride || (isMobile ? 'phone' : 'desktop');
    
    const body = document.body;
    const downloadSection = document.querySelector('.downloads');
    const toggleBtn = document.getElementById('view-toggle');
    
    if (currentMode === 'phone') {
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
        
        if (toggleBtn) {
            toggleBtn.innerText = 'Desktop View';
        }
    } else {
        body.classList.add('desktop');
        body.classList.remove('phone');
        
        const notice = document.getElementById('mobile-notice');
        if (notice) {
            notice.remove();
        }
        
        if (toggleBtn) {
            toggleBtn.innerText = 'Mobile View';
        }
    }
}

window.addEventListener('DOMContentLoaded', () => {
    adjustLayout();
    
    const toggleBtn = document.getElementById('view-toggle');
    if (toggleBtn) {
        toggleBtn.addEventListener('click', () => {
            const body = document.body;
            if (body.classList.contains('phone')) {
                manualOverride = 'desktop';
            } else {
                manualOverride = 'phone';
            }
            adjustLayout();
        });
    }
});

window.addEventListener('resize', adjustLayout);
