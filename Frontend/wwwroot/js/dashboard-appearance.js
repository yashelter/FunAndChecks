// Appearance preferences only. This file never reads or changes authentication tokens.
window.dashboardAppearance = {
    load() {
        let theme = null, density = null, accent = null, disabledSeasons = [];
        try { theme = localStorage.getItem('fc-theme'); density = localStorage.getItem('fc-density'); } catch { }
        // Сезонные темы включены по умолчанию; храним только те, что пользователь выключил.
        try {
            const stored = localStorage.getItem('fc-seasons-off');
            if (stored !== null) {
                const parsed = JSON.parse(stored);
                if (Array.isArray(parsed)) disabledSeasons = parsed.filter(id => typeof id === 'string');
            } else if (localStorage.getItem('fc-halloween') === 'off') {
                disabledSeasons = ['halloween']; // старый отдельный переключатель хэллоуина
            }
        } catch { }
        try {
            const cookie = document.cookie.split(';').map(s => s.trim()).find(s => s.startsWith('fc-accent='));
            const candidate = decodeURIComponent(cookie?.slice('fc-accent='.length) || '');
            if (/^#[0-9a-f]{6}$/i.test(candidate)) accent = candidate;
        } catch { }
        return { isDarkMode: theme !== 'light', isCompact: density === 'compact', accent, disabledSeasons };
    },
    apply(isDark, compact, accent, visibleAccent, foreground, disabledSeasons, save) {
        document.documentElement.dataset.theme = isDark ? 'dark' : 'light';
        document.documentElement.dataset.density = compact ? 'compact' : 'comfortable';
        document.documentElement.style.setProperty('--fc-accent', visibleAccent);
        document.documentElement.style.setProperty('--fc-accent-text', foreground);
        document.documentElement.style.colorScheme = isDark ? 'dark' : 'light';
        if (save) {
            try {
                localStorage.setItem('fc-theme', isDark ? 'dark' : 'light');
                localStorage.setItem('fc-density', compact ? 'compact' : 'comfortable');
                localStorage.setItem('fc-seasons-off', JSON.stringify(Array.isArray(disabledSeasons) ? disabledSeasons : []));
            } catch { }
            if (/^#[0-9a-f]{6}$/i.test(accent)) {
                document.cookie = `fc-accent=${encodeURIComponent(accent)}; Max-Age=31536000; Path=/; SameSite=Lax${location.protocol === 'https:' ? '; Secure' : ''}`;
            }
        }
    }
};
