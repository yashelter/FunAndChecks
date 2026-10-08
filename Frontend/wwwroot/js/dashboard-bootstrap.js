// Device culture and dashboard captions. Legacy keeps its previous startup rule.
(function () {
    window.culturePreference = {
        get() {
            let stored = null;
            try { stored = localStorage.getItem('culture_preference'); } catch { }
            if (stored === 'en-US' || stored === 'ru-RU') return stored;
            const languages = navigator.languages?.length ? navigator.languages : [navigator.language || 'en-US'];
            if (document.documentElement.dataset.uiVersion === 'legacy')
                return languages.some(language => language && language.toLowerCase().startsWith('ru')) ? 'ru-RU' : 'en-US';
            const supported = languages.map(language => String(language).toLowerCase().split('-')[0])
                .find(language => language === 'ru' || language === 'en');
            const selected = supported === 'ru' ? 'ru-RU' : 'en-US';
            try { localStorage.setItem('culture_preference', selected); } catch { }
            return selected;
        },
        set(culture) {
            if (culture !== 'en-US' && culture !== 'ru-RU') return false;
            if (document.documentElement.dataset.uiVersion === 'legacy') {
                try { localStorage.setItem('culture_preference', culture); } catch { return false; }
                document.documentElement.lang = culture;
                return true;
            }
            document.documentElement.lang = culture;
            try { localStorage.setItem('culture_preference', culture); } catch { return false; }
            return true;
        }
    };

    let resource;
    const selected = new Map();
    window.dashboardCaptions = {
        async get(culture) {
            if (selected.has(culture)) return selected.get(culture);
            resource ??= fetch('/loading/captions.json')
                .then(response => response.ok ? response.json() : {})
                .catch(() => ({}));
            const captions = await resource;
            const candidates = (Array.isArray(captions[culture]) ? captions[culture] : [])
                .filter(value => typeof value === 'string' && value.trim().length > 0);
            const caption = candidates.length ? candidates[Math.floor(Math.random() * candidates.length)].trim() : '';
            selected.set(culture, caption);
            return caption;
        }
    };
})();
