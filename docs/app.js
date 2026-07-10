document.addEventListener('DOMContentLoaded', () => {
    // 1. Mockup Settings Window Toggle
    const expertToggle = document.getElementById('mockup-expert-toggle');
    const expertCard = document.getElementById('mockup-expert-card');
    
    // Collapse by default in preview mock
    if (expertCard) {
        expertCard.classList.add('collapsed');
        expertToggle.classList.remove('active');
    }

    if (expertToggle) {
        expertToggle.addEventListener('click', () => {
            const isCollapsing = !expertCard.classList.contains('collapsed');
            if (isCollapsing) {
                expertCard.classList.add('collapsed');
                expertToggle.classList.remove('active');
            } else {
                expertCard.classList.remove('collapsed');
                expertToggle.classList.add('active');
            }
        });
    }

    // 2. Play/Pause toggle in virtual taskbar player
    const playBtn = document.getElementById('btn-play');
    if (playBtn) {
        playBtn.addEventListener('click', () => {
            if (playBtn.innerText === '▶') {
                playBtn.innerText = '⏸';
                playBtn.style.color = '#fff';
            } else {
                playBtn.innerText = '▶';
                playBtn.style.color = 'var(--text-primary)';
            }
        });
    }

    // 3. Choice selector for Layout styles
    const choices = document.querySelectorAll('.choice');
    const player = document.getElementById('v-player');

    choices.forEach(btn => {
        btn.addEventListener('click', () => {
            choices.forEach(c => c.classList.remove('active'));
            btn.classList.add('active');
            
            const layout = btn.getAttribute('data-layout');
            // Remove previous layouts
            player.className = 'virtual-player ' + layout;
        });
    });

    // 4. Accent Hue selectors
    const hues = document.querySelectorAll('.hue');
    const root = document.querySelector(':root');

    hues.forEach(hue => {
        hue.addEventListener('click', () => {
            hues.forEach(h => h.classList.remove('active'));
            hue.classList.add('active');
            
            const color = hue.getAttribute('data-color');
            root.style.setProperty('--accent', color);
            
            // Generate glowing shadow variation
            const glowColor = hexToRgba(color, 0.45);
            root.style.setProperty('--accent-glow', glowColor);
            
            // Mirror color to player border preview
            const playerBorder = document.getElementById('v-border');
            if (playerBorder) {
                playerBorder.style.borderColor = color;
            }
        });
    });

    // Utility hex parser to Rgba
    function hexToRgba(hex, alpha) {
        const r = parseInt(hex.slice(1, 3), 16);
        const g = parseInt(hex.slice(3, 5), 16);
        const b = parseInt(hex.slice(5, 7), 16);
        return `rgba(${r}, ${g}, ${b}, ${alpha})`;
    }

    // 5. Trigger Settings preview morph simulator
    const triggerToggle = document.getElementById('trigger-preview-toggle');
    const mockupFrame = document.querySelector('.mockup-frame');
    
    if (triggerToggle && mockupFrame) {
        triggerToggle.addEventListener('click', () => {
            mockupFrame.style.transition = 'transform 0.5s cubic-bezier(0.16, 1, 0.3, 1)';
            mockupFrame.style.transform = 'scale(0.96)';
            
            setTimeout(() => {
                mockupFrame.style.transform = 'scale(1)';
                
                // Toggle expert mode automatically during this transition
                if (expertToggle) {
                    expertToggle.click();
                }
            }, 250);
        });
    }
});
