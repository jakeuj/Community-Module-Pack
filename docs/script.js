(() => {
  "use strict";

  const root = document.documentElement;
  root.classList.add("js");

  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
  const finePointer = window.matchMedia("(pointer: fine)");

  const setRevealDelays = () => {
    document.querySelectorAll("[data-reveal-delay]").forEach((element) => {
      const delay = Number.parseInt(element.dataset.revealDelay || "0", 10);
      if (Number.isFinite(delay)) {
        element.style.setProperty("--reveal-delay", `${Math.max(0, delay)}ms`);
      }
    });
  };

  const revealContent = () => {
    const targets = document.querySelectorAll(".reveal");
    if (reduceMotion.matches || !("IntersectionObserver" in window)) {
      targets.forEach((target) => target.classList.add("is-visible"));
      return;
    }

    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      });
    }, { rootMargin: "0px 0px -9%", threshold: 0.08 });

    targets.forEach((target) => observer.observe(target));
  };

  const animateCounter = (element) => {
    const target = Number.parseInt(element.dataset.count || "0", 10);
    const suffix = element.dataset.suffix || "";
    if (!Number.isFinite(target)) return;

    if (reduceMotion.matches) {
      element.textContent = `${target}${suffix}`;
      return;
    }

    const duration = 850;
    let startedAt = 0;
    const step = (timestamp) => {
      if (!startedAt) startedAt = timestamp;
      const progress = Math.min((timestamp - startedAt) / duration, 1);
      const eased = 1 - Math.pow(1 - progress, 3);
      element.textContent = `${Math.round(target * eased)}${suffix}`;
      if (progress < 1) window.requestAnimationFrame(step);
    };
    window.requestAnimationFrame(step);
  };

  const initializeCounters = () => {
    const counters = document.querySelectorAll("[data-count]");
    if (!("IntersectionObserver" in window)) {
      counters.forEach(animateCounter);
      return;
    }

    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        animateCounter(entry.target);
        observer.unobserve(entry.target);
      });
    }, { threshold: 0.65 });

    counters.forEach((counter) => observer.observe(counter));
  };

  const initializePipelines = () => {
    const panels = document.querySelectorAll("[data-pipeline]");
    if (reduceMotion.matches || !("IntersectionObserver" in window)) return;

    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        entry.target.classList.toggle("is-flowing", entry.isIntersecting);
      });
    }, { rootMargin: "-15% 0px -15%", threshold: 0.2 });

    panels.forEach((panel) => observer.observe(panel));
  };

  const initializeNavigation = () => {
    const button = document.querySelector(".nav-toggle");
    const nav = document.querySelector("#primary-nav");
    if (!button || !nav) return;

    const closeMenu = () => {
      button.setAttribute("aria-expanded", "false");
      button.querySelector(".sr-only").textContent = "開啟導覽選單";
      nav.classList.remove("is-open");
    };

    button.addEventListener("click", () => {
      const open = button.getAttribute("aria-expanded") === "true";
      button.setAttribute("aria-expanded", String(!open));
      button.querySelector(".sr-only").textContent = open ? "開啟導覽選單" : "關閉導覽選單";
      nav.classList.toggle("is-open", !open);
    });

    nav.querySelectorAll("a").forEach((link) => link.addEventListener("click", closeMenu));
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape") closeMenu();
    });

    const sectionLinks = [...nav.querySelectorAll("a[href^='#']")];
    const sections = sectionLinks
      .map((link) => document.querySelector(link.getAttribute("href")))
      .filter(Boolean);

    if (!("IntersectionObserver" in window)) return;
    const spy = new IntersectionObserver((entries) => {
      const visible = entries
        .filter((entry) => entry.isIntersecting)
        .sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
      if (!visible) return;
      sectionLinks.forEach((link) => {
        const active = link.getAttribute("href") === `#${visible.target.id}`;
        if (active) link.setAttribute("aria-current", "true");
        else link.removeAttribute("aria-current");
      });
    }, { rootMargin: "-20% 0px -65%", threshold: [0.05, 0.2, 0.5] });

    sections.forEach((section) => spy.observe(section));
  };

  const initializeScrollProgress = () => {
    let ticking = false;
    const update = () => {
      const max = Math.max(1, document.documentElement.scrollHeight - window.innerHeight);
      root.style.setProperty("--scroll-progress", String(Math.min(1, window.scrollY / max)));
      ticking = false;
    };
    window.addEventListener("scroll", () => {
      if (ticking) return;
      ticking = true;
      window.requestAnimationFrame(update);
    }, { passive: true });
    update();
  };

  const initializePointerEffects = () => {
    if (!finePointer.matches || reduceMotion.matches) return;

    document.querySelectorAll("[data-tilt]").forEach((card) => {
      card.addEventListener("pointermove", (event) => {
        const rect = card.getBoundingClientRect();
        const x = (event.clientX - rect.left) / rect.width - 0.5;
        const y = (event.clientY - rect.top) / rect.height - 0.5;
        card.style.setProperty("--tilt-y", `${(x * 4).toFixed(2)}deg`);
        card.style.setProperty("--tilt-x", `${(-y * 3).toFixed(2)}deg`);
      });
      card.addEventListener("pointerleave", () => {
        card.style.removeProperty("--tilt-x");
        card.style.removeProperty("--tilt-y");
      });
    });

    document.querySelectorAll(".spotlight").forEach((card) => {
      card.addEventListener("pointermove", (event) => {
        const rect = card.getBoundingClientRect();
        card.style.setProperty("--pointer-x", `${event.clientX - rect.left}px`);
        card.style.setProperty("--pointer-y", `${event.clientY - rect.top}px`);
      });
    });

    const hero = document.querySelector(".hero");
    if (hero) {
      hero.addEventListener("pointermove", (event) => {
        const x = event.clientX / window.innerWidth - 0.5;
        const y = event.clientY / window.innerHeight - 0.5;
        hero.style.setProperty("--art-x", `${(x * -10).toFixed(1)}px`);
        hero.style.setProperty("--art-y", `${(y * -7).toFixed(1)}px`);
      });
      hero.addEventListener("pointerleave", () => {
        hero.style.removeProperty("--art-x");
        hero.style.removeProperty("--art-y");
      });
    }
  };

  const updateReleaseStatus = async () => {
    const status = document.querySelector("#release-status");
    const versionTargets = document.querySelectorAll("[data-release-version]");
    if (!status || versionTargets.length === 0 || !("fetch" in window)) return;

    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 3000);

    try {
      const response = await fetch("https://api.github.com/repos/jakeuj/Community-Module-Pack/releases/latest", {
        headers: { Accept: "application/vnd.github+json" },
        signal: controller.signal
      });
      if (!response.ok) throw new Error("Release API unavailable");
      const release = await response.json();
      const match = /^events-zh-tw-v(\d+\.\d+\.\d+-fork\.\d+)$/.exec(release.tag_name || "");
      const assets = Array.isArray(release.assets) ? release.assets : [];
      const packages = assets.filter((asset) => asset.name === "Events.Module.bhm");
      const packageAsset = packages[0];
      const trustedUrl = /^https:\/\/github\.com\/jakeuj\/Community-Module-Pack\/releases\/download\//.test(packageAsset?.browser_download_url || "");
      const validDigest = /^sha256:[0-9a-f]{64}$/i.test(packageAsset?.digest || "");

      if (release.draft || release.prerelease || !match || packages.length !== 1 || !trustedUrl || !validDigest) {
        throw new Error("Release contract rejected");
      }

      versionTargets.forEach((target) => { target.textContent = `v${match[1]}`; });
      status.classList.add("is-live");
      if (release.published_at) {
        const date = new Intl.DateTimeFormat("zh-TW", { dateStyle: "medium" }).format(new Date(release.published_at));
        status.title = `GitHub 最新穩定版，發布於 ${date}`;
      }
    } catch (_error) {
      const fallback = status.dataset.fallbackVersion;
      versionTargets.forEach((target) => { target.textContent = `v${fallback}`; });
    } finally {
      window.clearTimeout(timeout);
    }
  };

  setRevealDelays();
  revealContent();
  initializeCounters();
  initializePipelines();
  initializeNavigation();
  initializeScrollProgress();
  initializePointerEffects();
  updateReleaseStatus();
})();
