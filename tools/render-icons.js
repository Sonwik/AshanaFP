// Renders the Ashana PWA icons from the logo drawing (same paths as AppLogo.razor).
// Usage (after "npm install" in tests/e2e): NODE_PATH=tests/e2e/node_modules node tools/render-icons.js src/Ashana.Web/wwwroot/icons
const { chromium } = require('playwright');
const path = require('path');

const glyph = `
  <path d="M6.5 14.5H25.5C25.5 19.8 21.2 24 16 24C10.8 24 6.5 19.8 6.5 14.5Z" fill="#ffffff"/>
  <rect x="12" y="24.6" width="8" height="2.2" rx="1.1" fill="#ffffff"/>
  <path d="M14.6 12.2C12.9 10.4 16.9 9.2 15.4 6.6M18.6 12.2C17.6 11.1 19.6 10.2 18.9 8.8" fill="none" stroke="#f2a516" stroke-width="2" stroke-linecap="round"/>`;

// The glyph's visual centre is (16, 16.7); scale around it.
const scaled = (s) => `<g transform="translate(16 16.7) scale(${s}) translate(-16 -16.7)">${glyph}</g>`;

const icons = [
  { file: 'icon-192.png', size: 192, transparent: true,
    svg: `<rect width="32" height="32" rx="9" fill="#0b7380"/>${glyph}` },
  { file: 'icon-512.png', size: 512, transparent: true,
    svg: `<rect width="32" height="32" rx="9" fill="#0b7380"/>${glyph}` },
  // Maskable: full-bleed background, artwork inside the 80% safe circle.
  { file: 'icon-maskable-512.png', size: 512, transparent: false,
    svg: `<rect width="32" height="32" fill="#0b7380"/>${scaled(0.78)}` },
  // iOS fills transparency with black and applies its own rounded mask: full-bleed square.
  { file: 'apple-touch-icon.png', size: 180, transparent: false,
    svg: `<rect width="32" height="32" fill="#0b7380"/>${scaled(0.9)}` },
];

(async () => {
  const out = process.argv[2];
  const browser = await chromium.launch();
  const page = await browser.newPage({ deviceScaleFactor: 1 });
  for (const icon of icons) {
    await page.setViewportSize({ width: icon.size, height: icon.size });
    await page.setContent(`<!doctype html><html><body style="margin:0;background:transparent">
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" width="${icon.size}" height="${icon.size}" style="display:block">${icon.svg}</svg>
      </body></html>`);
    await page.screenshot({ path: path.join(out, icon.file), omitBackground: icon.transparent, clip: { x: 0, y: 0, width: icon.size, height: icon.size } });
    console.log('wrote', icon.file);
  }
  await browser.close();
})();
