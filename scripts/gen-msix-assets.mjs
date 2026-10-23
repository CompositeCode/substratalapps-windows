// Generates the MSIX visual assets (src/Substratal.Hub/Assets/*.png) from the brand SVG masters in
// substratal-application-specs/windows/brand-assets/svg, following PLATFORM-SPEC §4b:
//   - plated tiles use the tint surface #E8E8F7 so they read on light and dark Start;
//   - unplated taskbar icons switch palette by theme (altform-unplated = dark taskbar, altform-lightunplated = light);
//   - 16–24 px use the small mark.
// The specs' brand kit ships only .ico and SVG for Windows, so this script fills the gap. Requires sharp:
//
//   npm install --no-save sharp && SPECS_DIR=../substratalapps-applications-specs node scripts/gen-msix-assets.mjs
import fs from 'node:fs';
import path from 'node:path';
import sharp from 'sharp';

const SPECS = process.env.SPECS_DIR ?? '../substratalapps-applications-specs';
const SVG = path.join(SPECS, 'substratal-application-specs/windows/brand-assets/svg');
const OUT = path.join(import.meta.dirname, '../src/Substratal.Hub/Assets');
const TINT = '#E8E8F7';
const svg = (name) => fs.readFileSync(path.join(SVG, `${name}.svg`));

fs.mkdirSync(OUT, { recursive: true });

async function mark(name, size) {
  return sharp(svg(name), { density: 1200 }).resize(size, size, { fit: 'contain', background: '#0000' }).png().toBuffer();
}

// A mark centred on a canvas. `scale` is the mark's share of the shorter side.
async function tile(file, width, height, { background = TINT, scale = 0.6, name = 'mark' } = {}) {
  const size = Math.round(Math.min(width, height) * scale);
  const canvas = sharp({ create: { width, height, channels: 4, background } });
  await canvas.composite([{ input: await mark(name, size), gravity: 'centre' }]).png().toFile(path.join(OUT, file));
}

const scales = [100, 200, 400];
const plated = [
  ['Square150x150Logo', 150, 150],
  ['Wide310x150Logo', 310, 150],
  ['SmallTile', 71, 71],
  ['LargeTile', 310, 310],
  ['SplashScreen', 620, 300],
  ['StoreLogo', 50, 50],
];
for (const [name, w, h] of plated)
  for (const s of scales) await tile(`${name}.scale-${s}.png`, Math.round((w * s) / 100), Math.round((h * s) / 100), { scale: name === 'SplashScreen' ? 0.5 : 0.6 });

// App list / taskbar icon: plated at each scale, then unplated per target size and taskbar theme.
for (const s of scales) await tile(`Square44x44Logo.scale-${s}.png`, Math.round(0.44 * s), Math.round(0.44 * s), { scale: 0.75 });
for (const t of [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256]) {
  const small = t <= 24;
  const transparent = '#0000';
  await tile(`Square44x44Logo.targetsize-${t}.png`, t, t, { background: TINT, scale: 0.8, name: small ? 'mark-small' : 'mark' });
  await tile(`Square44x44Logo.targetsize-${t}_altform-unplated.png`, t, t, { background: transparent, scale: 1, name: small ? 'mark-small-on-dark' : 'mark-on-dark' });
  await tile(`Square44x44Logo.targetsize-${t}_altform-lightunplated.png`, t, t, { background: transparent, scale: 1, name: small ? 'mark-small' : 'mark' });
}

// Badge: white, alpha only, as Windows requires for lock-screen badges.
for (const s of scales) {
  const px = Math.round(0.24 * s);
  const alpha = await sharp(await mark('mark', px)).extractChannel('alpha').toBuffer();
  await sharp({ create: { width: px, height: px, channels: 3, background: '#FFFFFF' } }).joinChannel(alpha).png().toFile(path.join(OUT, `BadgeLogo.scale-${s}.png`));
}

for (const f of ['Substratal.ico', 'Substratal-dark.ico'])
  fs.copyFileSync(path.join(SVG, '..', f), path.join(OUT, f));
for (const f of ['mark-small.svg', 'mark-small-on-dark.svg', 'lockup-inline.svg', 'lockup-inline-on-dark.svg'])
  fs.copyFileSync(path.join(SVG, f), path.join(OUT, f));

console.log(`Wrote ${fs.readdirSync(OUT).length} files to ${path.relative(process.cwd(), OUT)}`);
