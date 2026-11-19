// Aplica el tema guardado antes del primer pintado (ver src/app/tema.ts). Es un archivo aparte porque la CSP de
// Atlas no permite scripts en línea.
(function () {
  var tema = null;
  try {
    tema = window.localStorage.getItem('atlas.tema');
  } catch (e) {
    tema = null;
  }
  var oscuro = tema === 'oscuro' || (tema !== 'claro' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.classList.toggle('dark', oscuro);
})();
