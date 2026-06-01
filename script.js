const header = document.querySelector('[data-header]');
const navToggle = document.querySelector('[data-nav-toggle]');
const nav = document.querySelector('[data-nav]');
const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

function setHeaderState() {
  header.classList.toggle('is-scrolled', window.scrollY > 12);
}

function closeNavigation() {
  navToggle.setAttribute('aria-expanded', 'false');
  nav.classList.remove('is-open');
  document.body.classList.remove('nav-open');
}

navToggle.addEventListener('click', () => {
  const isOpen = navToggle.getAttribute('aria-expanded') === 'true';
  navToggle.setAttribute('aria-expanded', String(!isOpen));
  nav.classList.toggle('is-open', !isOpen);
  document.body.classList.toggle('nav-open', !isOpen);
});

nav.addEventListener('click', (event) => {
  if (event.target.matches('a')) {
    closeNavigation();
  }
});

window.addEventListener('scroll', setHeaderState, { passive: true });
setHeaderState();

if (!prefersReducedMotion) {
  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (entry.isIntersecting) {
        entry.target.classList.add('is-visible');
        observer.unobserve(entry.target);
      }
    }
  }, { threshold: 0.16 });

  document.querySelectorAll('.reveal').forEach((element) => observer.observe(element));
} else {
  document.querySelectorAll('.reveal').forEach((element) => element.classList.add('is-visible'));
}

window.addEventListener('keydown', (event) => {
  if (event.key === 'Escape') {
    closeNavigation();
  }
});
