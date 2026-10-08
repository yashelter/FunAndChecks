// Appearance preferences only. This file never reads or changes authentication tokens.
window.dashboardAppearance = {
    load() {
        let theme = null, density = null, accent = null;
        try { theme = localStorage.getItem('fc-theme'); density = localStorage.getItem('fc-density'); } catch { }
        try {
            const cookie = document.cookie.split(';').map(s => s.trim()).find(s => s.startsWith('fc-accent='));
            const candidate = decodeURIComponent(cookie?.slice('fc-accent='.length) || '');
            if (/^#[0-9a-f]{6}$/i.test(candidate)) accent = candidate;
        } catch { }
        return { isDarkMode: theme !== 'light', isCompact: density === 'compact', accent };
    },
    apply(isDark, compact, accent, visibleAccent, foreground, save) {
        document.documentElement.dataset.theme = isDark ? 'dark' : 'light';
        document.documentElement.dataset.density = compact ? 'compact' : 'comfortable';
        document.documentElement.style.setProperty('--fc-accent', visibleAccent);
        document.documentElement.style.setProperty('--fc-accent-text', foreground);
        document.documentElement.style.colorScheme = isDark ? 'dark' : 'light';
        if (save) {
            try { localStorage.setItem('fc-theme', isDark ? 'dark' : 'light'); localStorage.setItem('fc-density', compact ? 'compact' : 'comfortable'); } catch { }
            if (/^#[0-9a-f]{6}$/i.test(accent)) {
                document.cookie = `fc-accent=${encodeURIComponent(accent)}; Max-Age=31536000; Path=/; SameSite=Lax${location.protocol === 'https:' ? '; Secure' : ''}`;
            }
        }
    }
};
