(() => {
  const { supported, defaultLang, strings } = window.I18N;
  const STORAGE_KEY = "wincleaner-lang";
  const THEME_KEY = "wincleaner-theme";
  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  const year = document.getElementById("year");
  if (year) year.textContent = String(new Date().getFullYear());

  const header = document.querySelector(".site-header");
  const toggle = document.querySelector(".nav-toggle");
  const nav = document.querySelector(".nav");
  const langSelect = document.getElementById("lang-select");
  const themeToggle = document.getElementById("theme-toggle");
  const themeColorMeta = document.getElementById("theme-color-meta");

  const normalizeLang = (value) => {
    if (!value) return null;
    const lower = String(value).toLowerCase().replace("_", "-");
    if (supported.includes(lower)) return lower;
    const base = lower.split("-")[0];
    if (supported.includes(base)) return base;
    return null;
  };

  const detectLang = () => {
    const saved = normalizeLang(localStorage.getItem(STORAGE_KEY));
    if (saved) return saved;

    const candidates = [
      ...(navigator.languages || []),
      navigator.language,
      navigator.userLanguage,
    ].filter(Boolean);

    for (const candidate of candidates) {
      const match = normalizeLang(candidate);
      if (match) return match;
    }

    return defaultLang;
  };

  const t = (lang, key) =>
    (strings[lang] && strings[lang][key]) ||
    (strings[defaultLang] && strings[defaultLang][key]) ||
    key;

  const applyLanguage = (lang) => {
    const active = supported.includes(lang) ? lang : defaultLang;
    document.documentElement.lang = active;

    document.querySelectorAll("[data-i18n]").forEach((el) => {
      const key = el.getAttribute("data-i18n");
      const value = t(active, key);
      if (el.tagName === "META") {
        el.setAttribute("content", value);
      } else if (/<[a-z][\s\S]*>/i.test(value)) {
        el.innerHTML = value;
      } else {
        el.textContent = value;
      }
    });

    document.querySelectorAll("[data-i18n-aria]").forEach((el) => {
      const key = el.getAttribute("data-i18n-aria");
      el.setAttribute("aria-label", t(active, key));
    });

    if (toggle) {
      const open = toggle.getAttribute("aria-expanded") === "true";
      toggle.setAttribute(
        "aria-label",
        t(active, open ? "aria.menuClose" : "aria.menuOpen")
      );
    }

    syncThemeLabel(active);

    if (langSelect && langSelect.value !== active) {
      langSelect.value = active;
    }

    localStorage.setItem(STORAGE_KEY, active);
  };

  const getTheme = () =>
    document.documentElement.getAttribute("data-theme") === "light" ? "light" : "dark";

  const syncThemeLabel = (lang) => {
    if (!themeToggle) return;
    const active = lang || normalizeLang(localStorage.getItem(STORAGE_KEY)) || defaultLang;
    const dark = getTheme() === "dark";
    themeToggle.setAttribute(
      "aria-label",
      t(active, dark ? "aria.themeToLight" : "aria.themeToDark")
    );
  };

  const applyTheme = (theme) => {
    const next = theme === "light" ? "light" : "dark";
    document.documentElement.setAttribute("data-theme", next);
    localStorage.setItem(THEME_KEY, next);
    if (themeColorMeta) {
      themeColorMeta.setAttribute("content", next === "dark" ? "#0D0D0D" : "#F3F4F6");
    }
    syncThemeLabel();
  };

  applyTheme(getTheme());

  if (themeToggle) {
    themeToggle.addEventListener("click", () => {
      applyTheme(getTheme() === "dark" ? "light" : "dark");
    });
  }

  const initialLang = detectLang();
  applyLanguage(initialLang);

  if (langSelect) {
    langSelect.addEventListener("change", () => {
      applyLanguage(langSelect.value);
    });
  }

  const onScroll = () => {
    if (!header) return;
    header.classList.toggle("is-scrolled", window.scrollY > 12);
  };

  onScroll();
  window.addEventListener("scroll", onScroll, { passive: true });

  if (toggle && nav) {
    toggle.addEventListener("click", () => {
      const open = toggle.getAttribute("aria-expanded") === "true";
      const next = !open;
      toggle.setAttribute("aria-expanded", String(next));
      nav.classList.toggle("is-open", next);
      const lang = normalizeLang(localStorage.getItem(STORAGE_KEY)) || defaultLang;
      toggle.setAttribute(
        "aria-label",
        t(lang, next ? "aria.menuClose" : "aria.menuOpen")
      );
    });

    nav.querySelectorAll("a").forEach((link) => {
      link.addEventListener("click", () => {
        toggle.setAttribute("aria-expanded", "false");
        nav.classList.remove("is-open");
        const lang = normalizeLang(localStorage.getItem(STORAGE_KEY)) || defaultLang;
        toggle.setAttribute("aria-label", t(lang, "aria.menuOpen"));
      });
    });
  }

  /* Scroll reveal */
  const nodes = document.querySelectorAll("[data-reveal]");

  if (reduceMotion || !("IntersectionObserver" in window)) {
    nodes.forEach((el) => el.classList.add("is-visible"));
  } else {
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) return;
          entry.target.classList.add("is-visible");
          observer.unobserve(entry.target);
        });
      },
      { rootMargin: "0px 0px -8% 0px", threshold: 0.12 }
    );

    nodes.forEach((el, i) => {
      el.style.transitionDelay = `${Math.min(i % 6, 4) * 60}ms`;
      observer.observe(el);
    });
  }

  /* Stats chrome — bars fill while scrolling through the section */
  const statsBand = document.querySelector(".stats-band");
  const statRows = Array.from(document.querySelectorAll("[data-stat-row]"));
  const clamp01 = (n) => Math.min(1, Math.max(0, n));
  const easeOut = (t) => 1 - Math.pow(1 - t, 3);

  const paintStats = (progress) => {
    const p = reduceMotion ? 1 : easeOut(clamp01(progress));
    statRows.forEach((row, i) => {
      const stagger = reduceMotion ? 0 : i * 0.08;
      const local = clamp01((p - stagger) / Math.max(0.001, 1 - stagger * 0.6));
      const fill = Number(row.getAttribute("data-fill")) || 1;
      const target = Number(row.getAttribute("data-count")) || 0;
      const bar = row.querySelector(".stats-bar i");
      const count = row.querySelector(".count");
      if (bar) bar.style.setProperty("--p", String(fill * local));
      if (count) count.textContent = String(Math.round(target * local));
    });
  };

  if (statsBand && statRows.length) {
    if (reduceMotion) {
      paintStats(1);
    } else {
      let raf = 0;
      const update = () => {
        raf = 0;
        const rect = statsBand.getBoundingClientRect();
        const vh = window.innerHeight || 1;
        // 0 when section bottom hits viewport bottom-ish, 1 when section is well into view
        const start = vh * 0.92;
        const end = vh * 0.28;
        const progress = (start - rect.top) / Math.max(start - end, 1);
        paintStats(progress);
      };
      const onScroll = () => {
        if (!raf) raf = requestAnimationFrame(update);
      };
      update();
      window.addEventListener("scroll", onScroll, { passive: true });
      window.addEventListener("resize", onScroll, { passive: true });
    }
  }

  /* Live dashboard metrics in hero mock */
  const metrics = document.querySelectorAll(".live-metric");
  if (metrics.length && !reduceMotion) {
    const ranges = {
      cpu: [22, 48],
      gpu: [45, 78],
      ram: [38, 62],
    };

    const nudge = () => {
      metrics.forEach((el) => {
        const key = el.getAttribute("data-metric");
        const range = ranges[key] || [30, 70];
        const value = Math.round(
          range[0] + Math.random() * (range[1] - range[0])
        );
        el.textContent = `${value}%`;
      });
    };

    nudge();
    window.setInterval(nudge, 2200);
  }

  /* Cycle sidebar highlight in app mock */
  const sidebarItems = Array.from(document.querySelectorAll(".app-sidebar .sb-item"));
  if (sidebarItems.length > 1 && !reduceMotion) {
    let idx = sidebarItems.findIndex((el) => el.classList.contains("is-active"));
    if (idx < 0) idx = 0;
    window.setInterval(() => {
      sidebarItems[idx].classList.remove("is-active");
      idx = (idx + 1) % sidebarItems.length;
      sidebarItems[idx].classList.add("is-active");
    }, 2600);
  }

  /* Pointer glow + mock parallax */
  const finePointer = window.matchMedia("(pointer: fine)").matches;
  if (!reduceMotion && finePointer) {
    const glow = document.createElement("div");
    glow.className = "pointer-glow";
    document.body.appendChild(glow);
    document.body.classList.add("has-pointer");

    const mock = document.querySelector(".app-mock");
    const chrome = mock && mock.querySelector(".app-chrome");
    let raf = 0;
    let mx = 0;
    let my = 0;

    const paint = () => {
      raf = 0;
      glow.style.left = `${mx}px`;
      glow.style.top = `${my}px`;
      glow.classList.add("is-on");

      if (chrome && mock) {
        const rect = mock.getBoundingClientRect();
        const cx = rect.left + rect.width / 2;
        const cy = rect.top + rect.height / 2;
        const dx = (mx - cx) / Math.max(rect.width, 1);
        const dy = (my - cy) / Math.max(rect.height, 1);
        const tiltY = (-6 + dx * 5).toFixed(2);
        const tiltX = (3 - dy * 4).toFixed(2);
        chrome.style.setProperty("--tilt-y", `${tiltY}deg`);
        chrome.style.setProperty("--tilt-x", `${tiltX}deg`);
      }
    };

    window.addEventListener(
      "pointermove",
      (e) => {
        mx = e.clientX;
        my = e.clientY;
        if (!raf) raf = requestAnimationFrame(paint);
      },
      { passive: true }
    );

    document.addEventListener("pointerleave", () => {
      glow.classList.remove("is-on");
    });
  }
})();
