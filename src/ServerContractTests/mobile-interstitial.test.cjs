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
  const listeners = { document: new Map(), window: new Map() };
  const navigations = [];
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
    addEventListener(name, callback) { listeners.document.set(name, callback); },
    getElementById(id) { return anchors.get(id) ?? null; }
  };
  const location = {
    replace(url) { navigations.push(url); },
    set href(url) {
      launchUrl = url;
      launchAttempts.push(url);
      if (launchBehavior === 'throw') throw new Error('simulated scheme-launch exception');
    }
  };
  const launchAttempts = [];
  const window = {
    location,
    addEventListener(name, callback) { listeners.window.set(name, callback); },
  };
  try {
    const context = { window, document };
    for (const script of scripts) vm.runInNewContext(script, context);
  } catch (error) {
    launchError = error;
  }
  return {
    anchors,
    navigations,
    launchAttempts,
    get launchUrl() { return launchUrl; },
    get launchError() { return launchError; },
    listenerNames: { document: [...listeners.document.keys()], window: [...listeners.window.keys()] }
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
check('initial automatic launch happens once without any delayed portal navigation', () => {
  assertInitialized(silentPage);
  assert.equal(silentPage.launchAttempts.length, 1);
  assert.equal(silentPage.launchUrl, anchorById('open-chronicle')?.href);
  assert.equal(silentPage.navigations.length, 0);
  assert.deepEqual(silentPage.listenerNames, { document: [], window: [] });
});

check('launch exception is swallowed and does not navigate to the portal', () => {
  const page = makePage('throw');
  assertInitialized(page);
  assert.equal(page.navigations.length, 0);
});

check('no timers or click listeners can re-arm a fallback', () => {
  const page = makePage();
  assertInitialized(page);
  assert.equal(page.navigations.length, 0);
  assert.deepEqual(page.listenerNames, { document: [], window: [] });
  for (const anchor of page.anchors.values()) {
    anchor.click();
    assert.equal(anchor.defaultPrevented, false);
    assert.equal(page.navigations.length, 0);
  }
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
  assert.ok(anchor.href, 'download anchor needs an href');
  assert.equal(anchor.text, 'Download Chronicle');
});

process.exitCode = failures === 0 ? 0 : 1;
