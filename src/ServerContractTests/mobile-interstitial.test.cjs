const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const htmlPath = process.argv[2];
if (!htmlPath) {
  console.error('Usage: node mobile-interstitial.test.cjs <saved-interstitial.html>');
  process.exit(2);
}

const html = fs.readFileSync(htmlPath, 'utf8');
const scripts = [...html.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script\s*>/gi)].map((match) => match[1]);
assert.ok(scripts.length > 0, 'interstitial must contain an inline script');

function makePage(launchBehavior = 'ignore') {
  const listeners = { document: new Map(), window: new Map(), anchors: new Map() };
  const timers = [];
  const replacements = [];
  let visibilityState = 'visible';
  let launchUrl;
  let launchError;
  const anchors = new Map(['open-chronicle', 'download-chronicle'].map((id) => [id, {
    listeners: new Map(),
    defaultPrevented: false,
    addEventListener(name, callback) { this.listeners.set(name, callback); },
    click() {
      const event = { preventDefault: () => { this.defaultPrevented = true; } };
      this.listeners.get('click')?.(event);
    }
  }]));
  const document = {
    get visibilityState() { return visibilityState; },
    addEventListener(name, callback) { listeners.document.set(name, callback); },
    getElementById(id) { return anchors.get(id) ?? null; }
  };
  const location = {
    replace(url) { replacements.push(url); },
    set href(url) {
      launchUrl = url;
      if (launchBehavior === 'throw') throw new Error('simulated scheme-launch exception');
      if (launchBehavior === 'cancel') listeners.window.get('beforeunload')?.();
      if (launchBehavior === 'hidden-visible-during-launch') {
        visibilityState = 'hidden';
        listeners.document.get('visibilitychange')?.();
        visibilityState = 'visible';
        listeners.document.get('visibilitychange')?.();
      }
      if (launchBehavior === 'pagehide-during-launch') listeners.window.get('pagehide')?.();
    }
  };
  const window = {
    location,
    addEventListener(name, callback) { listeners.window.set(name, callback); },
    setTimeout(callback, delay) {
      const timer = { callback, delay, active: true };
      timers.push(timer);
      return timer;
    },
    clearTimeout(timer) { if (timer) timer.active = false; }
  };
  try {
    const context = { window, document, setTimeout: window.setTimeout, clearTimeout: window.clearTimeout };
    for (const script of scripts) vm.runInNewContext(script, context);
  } catch (error) {
    launchError = error;
  }
  return {
    anchors,
    timers,
    replacements,
    get launchUrl() { return launchUrl; },
    get launchError() { return launchError; },
    setVisibility(value) {
      visibilityState = value;
      listeners.document.get('visibilitychange')?.();
    },
    fireWindowEvent(name) { listeners.window.get(name)?.(); },
    fireTimers() { for (const timer of timers) if (timer.active) timer.callback(); }
  };
}

function assertInitialized(page) {
  assert.equal(page.launchError, undefined, 'inline script initialization must complete');
}

let failures = 0;
function check(name, callback) {
  try {
    callback();
    console.log(`PASS ${name}`);
  } catch (error) {
    failures++;
    console.log(`FAIL ${name}: ${error.message}`);
  }
}

const silentPage = makePage('ignore');
silentPage.fireTimers();
const expectedPortal = silentPage.replacements[0];
check('silent launch fallback succeeds', () => {
  assertInitialized(silentPage);
  assert.equal(silentPage.replacements.length, 1, 'fallback must run after a silently ignored launch');
});

check('launch exception falls back', () => {
  const page = makePage('throw');
  page.fireTimers();
  assertInitialized(page);
  assert.equal(page.replacements.length, 1);
  assert.equal(page.replacements[0], expectedPortal);
});

check('canceled beforeunload does not suppress fallback', () => {
  const page = makePage('cancel');
  page.fireTimers();
  assertInitialized(page);
  assert.equal(page.replacements.length, 1);
  assert.equal(page.replacements[0], expectedPortal);
});

check('hidden page suppresses fallback', () => {
  const page = makePage();
  page.setVisibility('hidden');
  page.fireTimers();
  assertInitialized(page);
  assert.equal(page.replacements.length, 0);
});

check('hidden then visible cancels stale fallback', () => {
  const page = makePage();
  page.setVisibility('hidden');
  page.setVisibility('visible');
  page.fireTimers();
  assertInitialized(page);
  assert.equal(page.replacements.length, 0);
});

check('pagehide suppresses fallback', () => {
  const page = makePage();
  page.fireWindowEvent('pagehide');
  page.fireTimers();
  assertInitialized(page);
  assert.equal(page.replacements.length, 0);
});

check('synchronous hidden then visible during launch suppresses fallback', () => {
  const page = makePage('hidden-visible-during-launch');
  page.fireTimers();
  assertInitialized(page);
  assert.equal(page.replacements.length, 0);
});

check('synchronous pagehide during launch suppresses fallback', () => {
  const page = makePage('pagehide-during-launch');
  page.fireTimers();
  assertInitialized(page);
  assert.equal(page.replacements.length, 0);
});

check('open anchor resets a 1500 ms fallback without preventing navigation', () => {
  const page = makePage();
  assertInitialized(page);
  const originalTimerCount = page.timers.length;
  page.anchors.get('open-chronicle').click();
  assert.ok(page.timers.length > originalTimerCount, 'open click should reset the fallback timer');
  assert.ok(page.timers.slice(0, originalTimerCount).every((timer) => !timer.active));
  assert.ok(page.timers.some((timer) => timer.active && timer.delay === 1500));
  assert.equal(page.anchors.get('open-chronicle').defaultPrevented, false);
});

check('download anchor cancels fallback without preventing navigation', () => {
  const page = makePage();
  assertInitialized(page);
  assert.ok(page.timers.some((timer) => timer.active), 'expected initial fallback timer');
  page.anchors.get('download-chronicle').click();
  assert.equal(page.timers.some((timer) => timer.active), false);
  assert.equal(page.anchors.get('download-chronicle').defaultPrevented, false);
});

function decodeAttribute(value) {
  return value
    .replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&#39;|&#x27;/gi, "'")
    .replace(/&lt;/g, '<').replace(/&gt;/g, '>')
    .replace(/&#(x[0-9a-f]+|\d+);/gi, (_, code) => String.fromCodePoint(code[0].toLowerCase() === 'x'
      ? Number.parseInt(code.slice(1), 16)
      : Number.parseInt(code, 10)));
}
function anchorById(id) {
  for (const match of html.matchAll(/<a\b([^>]*)>([\s\S]*?)<\/a\s*>/gi)) {
    const attributes = match[1];
    const foundId = attributes.match(/\bid\s*=\s*(["'])(.*?)\1/i)?.[2];
    if (foundId !== id) continue;
    const href = attributes.match(/\bhref\s*=\s*(["'])(.*?)\1/i)?.[2];
    return { href: href === undefined ? undefined : decodeAttribute(href), text: match[2].replace(/<[^>]*>/g, '').trim() };
  }
  return undefined;
}

check('open anchor is present with its runtime launch URI and label', () => {
  const anchor = anchorById('open-chronicle');
  assert.ok(anchor, 'missing #open-chronicle anchor');
  assert.ok(anchor.href, 'open anchor needs an href');
  const page = makePage();
  assertInitialized(page);
  assert.equal(anchor.href, page.launchUrl);
  assert.equal(anchor.text, 'Open Chronicle');
});

check('download anchor is present with the fallback portal URL and label', () => {
  const anchor = anchorById('download-chronicle');
  assert.ok(anchor, 'missing #download-chronicle anchor');
  assert.equal(anchor.href, expectedPortal);
  assert.equal(anchor.text, 'Download Chronicle');
});

process.exitCode = failures === 0 ? 0 : 1;
