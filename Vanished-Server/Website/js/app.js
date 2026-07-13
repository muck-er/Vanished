(function () {
  const c = document.getElementById('particles');
  const ctx = c.getContext('2d');
  let W, H, P = [];
  function resize() { W = c.width = c.offsetWidth; H = c.height = c.offsetHeight; }
  function mkP() { return { x: Math.random() * W, y: Math.random() * H, s: Math.random() * 2.5 + 0.5, vx: (Math.random() - .5) * .3, vy: -Math.random() * .35 - .05, o: Math.random() * .35 + .04, life: 0, max: Math.random() * 280 + 120, tri: Math.random() > .65 }; }
  function tri(x, y, s, o) { ctx.save(); ctx.translate(x, y); ctx.rotate(Math.random() * .08); ctx.beginPath(); ctx.moveTo(0, -s * 2); ctx.lineTo(s * 1.7, s); ctx.lineTo(-s * 1.7, s); ctx.closePath(); ctx.strokeStyle = 'rgba(41,182,246,' + o + ')'; ctx.lineWidth = .5; ctx.stroke(); ctx.restore(); }
  function tick() { ctx.clearRect(0, 0, W, H); while (P.length < 35) P.push(mkP()); P.forEach((p, i) => { p.x += p.vx; p.y += p.vy; p.life++; const pr = p.life / p.max, fade = pr < .2 ? pr / .2 : pr > .8 ? (1 - pr) / .2 : 1, op = p.o * fade; if (p.tri) { tri(p.x, p.y, p.s, op); } else { ctx.beginPath(); ctx.arc(p.x, p.y, p.s, 0, Math.PI * 2); ctx.fillStyle = 'rgba(41,182,246,' + op + ')'; ctx.fill(); } if (p.life >= p.max || p.y < -20) P.splice(i, 1); }); requestAnimationFrame(tick); }
  window.addEventListener('resize', resize); resize(); tick();
})();
const btn = document.getElementById('menu-btn'), menu = document.getElementById('mobile-menu'), io = document.getElementById('icon-open'), ic = document.getElementById('icon-close');
btn.addEventListener('click', () => { const o = menu.classList.toggle('open'); io.classList.toggle('hidden', o); ic.classList.toggle('hidden', !o); });
menu.querySelectorAll('a').forEach(a => a.addEventListener('click', () => { menu.classList.remove('open'); io.classList.remove('hidden'); ic.classList.add('hidden'); }));
const obs = new IntersectionObserver(entries => entries.forEach(e => { if (e.isIntersecting) { e.target.classList.add('visible'); obs.unobserve(e.target); } }), 'threshold:.1,rootMargin:0px 0px -40px 0px'.split(',').reduce((a, b) => { const [k, v] = b.trim().split(':'); a[k.trim()] = isNaN(v) ? v.trim() : +v; return a; }, {}));
document.querySelectorAll('.reveal').forEach(el => obs.observe(el));
document.querySelectorAll('a[href^="#"]').forEach(a => a.addEventListener('click', e => { const t = document.querySelector(a.getAttribute('href')); if (t) { e.preventDefault(); t.scrollIntoView({ behavior: 'smooth' }); } })
);
(() => {
  const tabs = [...document.querySelectorAll('[data-install-tab]')];
  const panels = [...document.querySelectorAll('[data-install-panel]')];

  const activateInstall = (name, shouldScroll = false) => {
    tabs.forEach(tab => {
      const active = tab.dataset.installTab === name;
      tab.classList.toggle('is-active', active);
      tab.setAttribute('aria-selected', String(active));
      tab.tabIndex = active ? 0 : -1;
    });
    panels.forEach(panel => {
      const active = panel.dataset.installPanel === name;
      panel.hidden = !active;
      panel.classList.toggle('is-active', active);
    });
    if (shouldScroll) {
      document.getElementById('installation')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  };

  tabs.forEach(tab => tab.addEventListener('click', () => activateInstall(tab.dataset.installTab)));
  document.querySelectorAll('[data-install-target]').forEach(button => {
    button.addEventListener('click', () => activateInstall(button.dataset.installTarget, true));
  });

  document.querySelectorAll('[data-copy]').forEach(button => {
    button.addEventListener('click', async () => {
      const original = button.textContent;
      try {
        await navigator.clipboard.writeText(button.dataset.copy);
      } catch {
        const area = document.createElement('textarea');
        area.value = button.dataset.copy;
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.select();
        document.execCommand('copy');
        area.remove();
      }
      button.textContent = 'Copiado';
      button.classList.add('is-copied');
      window.setTimeout(() => {
        button.textContent = original;
        button.classList.remove('is-copied');
      }, 1500);
    });
  });

  const platform = (navigator.userAgentData?.platform || navigator.platform || navigator.userAgent).toLowerCase();
  const recommended = platform.includes('win') ? 'windows' : platform.includes('linux') ? 'linux' : null;
  if (recommended) {
    document.querySelectorAll(`[data-recommend-for="${recommended}"]`).forEach(chip => {
      chip.closest('.release-card')?.classList.add('is-recommended');
    });
  }

  const progress = document.createElement('div');
  progress.id = 'site-scroll-progress';
  progress.setAttribute('aria-hidden', 'true');
  document.body.appendChild(progress);
  const updateProgress = () => {
    const max = document.documentElement.scrollHeight - innerHeight;
    progress.style.width = `${max > 0 ? Math.min(100, scrollY / max * 100) : 0}%`;
  };
  addEventListener('scroll', updateProgress, { passive: true });
  addEventListener('resize', updateProgress, { passive: true });
  updateProgress();
})();


(() => {
  const tabs = [...document.querySelectorAll('[data-usage-tab]')];
  const panels = [...document.querySelectorAll('[data-usage-panel]')];
  if (!tabs.length || !panels.length) return;
  const activate = (name) => {
    tabs.forEach(tab => {
      const active = tab.dataset.usageTab === name;
      tab.classList.toggle('is-active', active);
      tab.setAttribute('aria-selected', String(active));
      tab.tabIndex = active ? 0 : -1;
    });
    panels.forEach(panel => {
      const active = panel.dataset.usagePanel === name;
      panel.hidden = !active;
      panel.classList.toggle('is-active', active);
    });
  };
  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => activate(tab.dataset.usageTab));
    tab.addEventListener('keydown', event => {
      if (!['ArrowRight', 'ArrowLeft', 'ArrowDown', 'ArrowUp'].includes(event.key)) return;
      event.preventDefault();
      const delta = ['ArrowRight', 'ArrowDown'].includes(event.key) ? 1 : -1;
      const next = tabs[(index + delta + tabs.length) % tabs.length];
      activate(next.dataset.usageTab);
      next.focus();
    });
  });
})();
