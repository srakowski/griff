// The page's side of Griff's web player: shows the loading screen, draws the frames the game builds with WebGL 2, runs a
// frame each time the browser shows one, plays the sound the game mixes through Web Audio, keeps its saves in
// localStorage, and sends the game the keyboard, mouse, touch and controller. The C# side calls the exported functions
// here (its [JSImport]s); `attach` gives this side the C# WebPlayer's [JSExport]s.
//
// A frame comes as a draw list (Griff.Graphics' DrawList, the same one the desktop's OpenGL renderer draws): its quads,
// its commands (five ints each: the op, a texture, the first quad, how many, and where its values start) and its values
// (colors, a view's place, the screen effects' settings). `draw` runs the commands as OpenGLRenderer does, with the same
// shaders (given by `start`), so both draw the same pixels.

const canvas = document.getElementById('game');
const loadingScreen = document.getElementById('loading');
const status = document.getElementById('status');
const fill = document.getElementById('fill');
const noticeBox = document.getElementById('notice');
const hint = document.getElementById('hint');

// The largest canvas drawn at full resolution, along its longer side: a bigger page (a 4K screen at twice its pixels) is
// drawn at this size and scaled up, sharp, by the browser.
const MAX_CANVAS = 4096;
// Griff's mixer's rate, and how far ahead of the current moment a new run of sound starts in Web Audio.
const SAMPLE_RATE = 44100;
const AUDIO_LEAD = 0.02;

// A quad's floats and bytes (its corners in clip space, the part of the texture, tint and flash: four floats each).
const QUAD_FLOATS = 20;
const QUAD_BYTES = QUAD_FLOATS * 4;
const COMMAND_INTS = 5;

// The draw list's ops (DrawOp), the picture target (DrawTarget) and the screen effects' settings (LookValue), by number.
const TARGET = 0, VIEW = 1, CLEAR = 2, SPRITES = 3, HOLES = 4, BEGIN_LIGHT = 5, LIGHTS = 6, SHADOWS = 7, SHADOWED_LIGHTS = 8,
  END_LIGHT = 9, EFFECTS = 10;
const PICTURE = 1;
const LOOK = ['uVignette', 'uScanlines', 'uCurvature', 'uPixel', 'uAberration', 'uSaturation', 'uContrast', 'uBrightness'];
const LOOK_TINT = 8, LOOK_GRAIN = 11, LOOK_PALETTE = 12, LOOK_TIME = 13;

let player = null;
let gl = null;
let spriteProgram = null;
let shapeProgram = null;
let shapeMode = null;
let effectsProgram = null;
let effects = null;
let vao = null;
let buffer = null;
let wholeVao = null;
let wholeBuffer = null;
let white = null;
let missing = null;
let picture = null;
let light = null;
const textures = new Map();
const whole = new Float32Array(QUAD_FLOATS);
let quadBytes = new Uint8Array(QUAD_BYTES * 1024);
let capturing = null;

let audio = null;
let pad = null;
let hadPad = false;
let rumbleStrength = 0;
let rumbledAt = 0;
let wantFullscreen = false;
let touchLeft = false;
let noticeTimer = 0;

// What a test (or a person in the console) can see of the game: 'loading', 'running', 'ended' or 'failed: <why>', the
// frames drawn, the images still being decoded, capture(), which resolves to the next frame's pixels (top-down RGBA, in
// base64) once it's drawn, probe() (WebPlayer.Probe: the scene, an entity's place, a saved value...), where its saves
// are kept, the notice shown, and its sound: 'none', 'waiting' (for a click or key) or 'running', and level(), the loudest
// sample Web Audio is playing right now (0 to 1, through an AnalyserNode).
globalThis.griff = {
  status: 'loading',
  frames: 0,
  pending: 0,
  saveKey: null,
  notice: '',
  capture: () => new Promise(resolve => { capturing = resolve; }),
  probe: what => player ? player.Probe(what) : '',
  get lost() { return gl?.isContextLost() ?? false; },
  get visibility() { return document.visibilityState; },
  audio: {
    get state() { return !audio ? 'none' : audio.context.state === 'running' ? 'running' : 'waiting'; },
    get hint() { return !hint.hidden; },
    level() {
      if (!audio)
        return 0;
      audio.analyser.getFloatTimeDomainData(audio.samples);
      let peak = 0;
      for (const sample of audio.samples)
        peak = Math.max(peak, Math.abs(sample));
      return peak;
    },
  },
};

setUpAudio();

/** Takes the C# WebPlayer's exports (called by main.js before the game starts). */
export function attach(exports) {
  player = exports;
}

/** The page's address, which the game's files are fetched relative to. */
export function baseUrl() {
  return document.baseURI;
}

/** The page address's query, with its '?' ('' for none). */
export function query() {
  return location.search;
}

/** Shows what went wrong over the game, and stops its frames. */
export function fail(message) {
  loadingScreen.hidden = false;
  loadingScreen.className = 'over failed';
  status.textContent = message;
  hint.hidden = true;
  globalThis.griff.status = `failed: ${message}`;
}

/** .NET's own files loading (from main.js): most of the loading screen's bar. */
export function loading(loaded, total) {
  if (total > 0)
    fill.style.width = `${Math.round(80 * Math.min(loaded / total, 1))}%`;
}

/** The game's files loading: the rest of the bar. */
export function progress(done, total) {
  fill.style.width = `${80 + Math.round(20 * (total > 0 ? done / total : 1))}%`;
  status.textContent = 'Loading the game…';
}

/** The game quit: its frames stop and `message` shows instead (a page can't close itself). */
export function ended(message) {
  globalThis.griff.status = 'ended';
  loadingScreen.hidden = false;
  loadingScreen.className = 'over ended';
  status.textContent = message;
  hint.hidden = true;
  if (document.fullscreenElement)
    document.exitFullscreen?.().catch(() => {});
}

/** Shows `message` over the top of the game for a while (or until it's clicked). */
export function notice(message) {
  globalThis.griff.notice = message;
  noticeBox.textContent = message;
  noticeBox.hidden = false;
  clearTimeout(noticeTimer);
  noticeTimer = setTimeout(() => { noticeBox.hidden = true; }, 12000);
}

/** The save kept under `key` in the page's storage, or null (none, or storage is off). */
export function loadSave(key) {
  globalThis.griff.saveKey = key;
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

/** Keeps a save under `key` in the page's storage: it survives reloading the page (not clearing the site's data). */
export function storeSave(key, json) {
  try {
    localStorage.setItem(key, json);
  } catch (error) {
    console.warn(`Couldn't save: ${error?.message ?? error}`);
  }
}

/** Shakes the first controller this hard, 0 to 1 (0 stops it), where the browser lets a page. */
export function rumble(strength) {
  rumbleStrength = strength;
  rumbledAt = 0;
  if (strength <= 0)
    pad?.vibrationActuator?.reset?.()?.catch?.(() => {});
}

/** Fills the screen with the page, or stops; the browser allows it only after a click or a key, so it may wait for one. */
export function fullscreen(on) {
  wantFullscreen = on;
  applyFullscreen();
}

/** Gives back the mouse a first-person game had. */
export function releaseMouse() {
  if (document.pointerLockElement)
    document.exitPointerLock();
}

/** Shows the pointer the game asks for over it (a CSS cursor: "default", "pointer", "crosshair", "help" or "none"). */
export function cursor(name) {
  canvas.style.cursor = name;
}

/** Says (or stops saying) that sound starts with a click or a key: the browser holds it until then. */
export function soundWaiting(waiting) {
  hint.hidden = !waiting || !audio || audio.context.state === 'running';
}

/** Seconds of sound queued and not yet played (from where the next run starts); -1 while the page may not play sound, -2 with no Web Audio. */
export function audioState() {
  if (!audio)
    return -2;
  const context = audio.context;
  if (context.state !== 'running')
    return -1;
  const start = context.currentTime + AUDIO_LEAD;
  if (audio.next < start)
    audio.next = start;
  return audio.next - context.currentTime;
}

/** Seconds Web Audio takes to get what it plays to the speakers. */
export function audioLatency() {
  return audio ? (audio.context.baseLatency || 0) + (audio.context.outputLatency || 0) : 0;
}

/** Queues sound, 32-bit floats left and right in turn at Griff's mixer's rate, straight after what's queued. */
export function audioPush(view) {
  if (!audio || audio.context.state !== 'running')
    return;
  const bytes = view.slice();
  const samples = new Float32Array(bytes.buffer, bytes.byteOffset, bytes.byteLength / 4);
  const frames = samples.length / 2;
  if (frames < 1)
    return;
  const buffer = audio.context.createBuffer(2, frames, SAMPLE_RATE);
  const left = buffer.getChannelData(0);
  const right = buffer.getChannelData(1);
  for (let i = 0; i < frames; i++) {
    left[i] = samples[i * 2];
    right[i] = samples[i * 2 + 1];
  }

  const source = audio.context.createBufferSource();
  source.buffer = buffer;
  source.connect(audio.analyser);
  source.start(audio.next);
  audio.next += frames / SAMPLE_RATE;
}

/** Decodes a sound file (MP3) with the browser's own decoders, as an AudioBuffer at Griff's mixer's rate where it can. */
export async function decodeSound(view) {
  const bytes = view.slice();
  const context = audio?.context ?? new OfflineAudioContext(2, 1, SAMPLE_RATE);
  return await context.decodeAudioData(bytes.buffer);
}

/** A decoded sound's samples, its channels (at most two) in turn, as 32-bit floats' bytes into `view`. */
export function soundSamples(buffer, view) {
  const channels = Math.min(2, buffer.numberOfChannels);
  const data = [];
  for (let c = 0; c < channels; c++)
    data.push(buffer.getChannelData(c));
  const out = new Float32Array(buffer.length * channels);
  for (let i = 0; i < buffer.length; i++) {
    for (let c = 0; c < channels; c++)
      out[i * channels + c] = data[c][i];
  }

  view.set(new Uint8Array(out.buffer));
}

/** Starts drawing frames, with these shaders (`header` goes before each), and listening to the keyboard and mouse. */
export function start(title, header, vertex, sprite, shape, effectsSource) {
  document.title = title;
  gl = canvas.getContext('webgl2', { alpha: false, antialias: false, depth: false, stencil: false });
  if (!gl) {
    fail("This browser can't show the game: it has no WebGL 2.");
    return;
  }

  setUp(header, vertex, sprite, shape, effectsSource);
  listen();
  loadingScreen.hidden = true;
  globalThis.griff.status = 'running';
  canvas.focus();
  requestAnimationFrame(frame);
}

/** Gives an image (a file's bytes; none for a missing one) to the texture `id`, again when its file changes. */
export function loadImage(id, file) {
  // The view lasts only for this call, so it's copied before decoding (which finishes later).
  const bytes = file.slice();
  const old = textures.get(id);
  if (old && old !== missing)
    gl.deleteTexture(old);
  if (bytes.length === 0) {
    textures.set(id, missing);
    return;
  }

  // Nothing shows until the browser has decoded it.
  const texture = makeTexture(new Uint8Array([0, 0, 0, 0]), 1, 1);
  textures.set(id, texture);
  globalThis.griff.pending++;
  createImageBitmap(new Blob([bytes]), { premultiplyAlpha: 'none', colorSpaceConversion: 'none' })
    .then(image => upload(texture, image))
    .catch(() => {
      if (textures.get(id) === texture)
        textures.set(id, missing);
      gl.deleteTexture(texture);
    })
    .finally(() => globalThis.griff.pending--);
}

/**
 * Copies `rows` rows from `top` of a glyph atlas page (RGBA, `size` pixels wide) to its texture `id`, making it `size`
 * pixels square the first time. Text is smoothed, as on the desktop.
 */
export function glyphs(id, size, top, rows, rgba) {
  let texture = textures.get(id);
  if (!texture) {
    texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, size, size, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    filter(gl.LINEAR);
    textures.set(id, texture);
  }

  gl.bindTexture(gl.TEXTURE_2D, texture);
  unpack();
  gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, top, size, rows, gl.RGBA, gl.UNSIGNED_BYTE, rgba.slice());
}

/** Draws a frame's draw list into the canvas, `width` by `height` pixels; returns how many draw calls it took. */
export function draw(quads, commandView, valueView, width, height) {
  const length = quads.byteLength;
  if (quadBytes.byteLength < length)
    quadBytes = new Uint8Array(Math.max(length, quadBytes.byteLength * 2));
  quads.copyTo(quadBytes.subarray(0, length));
  const commands = commandView.slice();
  const values = new Float32Array(valueView.slice().buffer);

  let calls = 0;
  let target = null;
  // Clip space covers the whole canvas; a view only clips drawing to its part.
  gl.viewport(0, 0, width, height);
  gl.bindVertexArray(vao);
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.bufferData(gl.ARRAY_BUFFER, quadBytes.subarray(0, length), gl.STREAM_DRAW);
  gl.enable(gl.BLEND);
  gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
  gl.enable(gl.SCISSOR_TEST);
  gl.activeTexture(gl.TEXTURE0);

  // A run of quads, from where they start in the buffer (WebGL 2 has no base instance).
  const quadsFrom = (first, count) => {
    gl.bindVertexArray(vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    for (let attribute = 0; attribute < 5; attribute++)
      gl.vertexAttribPointer(attribute, 4, gl.FLOAT, false, QUAD_BYTES, first * QUAD_BYTES + attribute * 16);
    gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, count);
    calls++;
  };
  // A quad over the whole canvas, showing a target's texture the right way up (one drawn into starts at the bottom).
  const wholeView = () => {
    whole.fill(0);
    whole.set([-1, 1, 1, 1, 1, -1, -1, -1, 0, 1, 1, -1, 1, 1, 1, 1]);
    gl.bindVertexArray(wholeVao);
    gl.bindBuffer(gl.ARRAY_BUFFER, wholeBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, whole, gl.STREAM_DRAW);
    gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, 1);
    calls++;
  };
  const useShape = mode => {
    gl.useProgram(shapeProgram);
    gl.uniform1i(shapeMode, mode);
  };

  for (let c = 0; c < commands.length; c += COMMAND_INTS) {
    const [op, texture, first, count, at] = [commands[c], commands[c + 1], commands[c + 2], commands[c + 3], commands[c + 4]];
    switch (op) {
      case TARGET:
        if (at === PICTURE) {
          picture = ensureTarget(picture, width, height, false);
          target = picture.framebuffer;
        } else {
          target = null;
        }

        gl.bindFramebuffer(gl.FRAMEBUFFER, target);
        break;
      case VIEW:
        clip(values[at], values[at + 1], values[at + 2], values[at + 3], height);
        break;
      case CLEAR:
        gl.clearColor(values[at], values[at + 1], values[at + 2], 1);
        gl.clear(gl.COLOR_BUFFER_BIT);
        break;
      case SPRITES:
        gl.useProgram(spriteProgram);
        gl.bindTexture(gl.TEXTURE_2D, textureOf(texture));
        quadsFrom(first, count);
        break;
      case HOLES:
        useShape(1);
        quadsFrom(first, count);
        break;
      case BEGIN_LIGHT:
        light = ensureTarget(light, width, height, true);
        gl.bindFramebuffer(gl.FRAMEBUFFER, light.framebuffer);
        gl.clearColor(values[at], values[at + 1], values[at + 2], 1);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.blendFunc(gl.ONE, gl.ONE);
        break;
      case LIGHTS:
        useShape(0);
        quadsFrom(first, count);
        break;
      case SHADOWS:
        // The shadows mark the stencil, and the light after is added up only where they didn't.
        gl.enable(gl.STENCIL_TEST);
        gl.clearStencil(0);
        gl.clear(gl.STENCIL_BUFFER_BIT);
        gl.colorMask(false, false, false, false);
        gl.stencilFunc(gl.ALWAYS, 1, 0xFF);
        gl.stencilOp(gl.KEEP, gl.KEEP, gl.REPLACE);
        useShape(0);
        quadsFrom(first, count);
        gl.colorMask(true, true, true, true);
        gl.stencilFunc(gl.EQUAL, 0, 0xFF);
        gl.stencilOp(gl.KEEP, gl.KEEP, gl.KEEP);
        break;
      case SHADOWED_LIGHTS:
        useShape(0);
        quadsFrom(first, count);
        gl.disable(gl.STENCIL_TEST);
        break;
      case END_LIGHT:
        // The light map over the view (drawing is clipped to it), multiplying what's there.
        gl.bindFramebuffer(gl.FRAMEBUFFER, target);
        gl.blendFunc(gl.DST_COLOR, gl.ZERO);
        gl.useProgram(spriteProgram);
        gl.bindTexture(gl.TEXTURE_2D, light.texture);
        wholeView();
        gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
        break;
      case EFFECTS:
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        clip(0, 0, width, height, height);
        gl.disable(gl.BLEND);
        gl.useProgram(effectsProgram);
        gl.uniform2f(effects.uSize, width, height);
        LOOK.forEach((name, i) => gl.uniform1f(effects[name], values[at + i]));
        gl.uniform4f(effects.uTint, values[at + LOOK_TINT], values[at + LOOK_TINT + 1], values[at + LOOK_TINT + 2], 1);
        gl.uniform1f(effects.uGrain, values[at + LOOK_GRAIN]);
        gl.uniform1i(effects.uPalette, values[at + LOOK_PALETTE]);
        gl.uniform1f(effects.uTime, values[at + LOOK_TIME]);
        gl.bindTexture(gl.TEXTURE_2D, picture.texture);
        wholeView();
        gl.enable(gl.BLEND);
        break;
    }
  }

  gl.disable(gl.SCISSOR_TEST);
  gl.bindVertexArray(null);
  if (capturing) {
    const resolve = capturing;
    capturing = null;
    const pixels = read(width, height);
    let text = '';
    for (let i = 0; i < pixels.length; i += 0x8000)
      text += String.fromCharCode.apply(null, pixels.subarray(i, i + 0x8000));
    resolve(btoa(text));
  }

  return calls;
}

/** Reads what was just drawn into `pixels`, as top-down RGBA rows. */
export function capture(pixels, width, height) {
  pixels.set(read(width, height));
}

// The canvas's pixels, top row first (WebGL reads from the bottom).
function read(width, height) {
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  const bottomUp = new Uint8Array(width * height * 4);
  gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, bottomUp);
  const topDown = new Uint8Array(bottomUp.length);
  const stride = width * 4;
  for (let y = 0; y < height; y++)
    topDown.set(bottomUp.subarray((height - 1 - y) * stride, (height - y) * stride), y * stride);
  return topDown;
}

function frame(now) {
  if (globalThis.griff.status !== 'running')
    return;
  // The screen's own pixels (devicePixelRatio), so the game is sharp on any screen, up to MAX_CANVAS along its longer side.
  let scale = devicePixelRatio || 1;
  const longest = Math.max(canvas.clientWidth, canvas.clientHeight) * scale;
  if (longest > MAX_CANVAS)
    scale *= MAX_CANVAS / longest;
  const width = Math.max(1, Math.round(canvas.clientWidth * scale));
  const height = Math.max(1, Math.round(canvas.clientHeight * scale));
  if (canvas.width !== width || canvas.height !== height)
    [canvas.width, canvas.height] = [width, height];
  readGamepad(now);
  player.Frame(now, width, height);
  // A finger lifted counts as the mouse leaving only once the game has seen the tap.
  if (touchLeft) {
    touchLeft = false;
    player.PointerLeft();
  }

  globalThis.griff.frames++;
  requestAnimationFrame(frame);
}

// The first controller plugged in, in the browser's standard layout (the Xbox one's), handed to the game each frame; its
// rumble kept going while the game asks for one.
function readGamepad(now) {
  pad = null;
  for (const candidate of navigator.getGamepads?.() ?? []) {
    if (candidate?.connected) {
      pad = candidate;
      break;
    }
  }

  if (!pad) {
    if (hadPad)
      player.Gamepad(false, 0, 0, 0, 0, 0, 0, 0);
    hadPad = false;
    return;
  }

  hadPad = true;
  let pressed = 0;
  pad.buttons.forEach((button, i) => {
    if (i < 31 && button.pressed)
      pressed |= 1 << i;
  });
  const axis = i => pad.axes[i] ?? 0;
  const value = i => pad.buttons[i]?.value ?? 0;
  player.Gamepad(true, pressed, axis(0), axis(1), axis(2), axis(3), value(6), value(7));
  if (rumbleStrength > 0 && now - rumbledAt > 100 && pad.vibrationActuator?.playEffect) {
    rumbledAt = now;
    pad.vibrationActuator.playEffect('dual-rumble', { duration: 200, strongMagnitude: rumbleStrength, weakMagnitude: rumbleStrength })
      .catch(() => {});
  }
}

// Web Audio, made as the page opens: the browser holds it until the player clicks or presses a key (its autoplay rule),
// so it's resumed by the first one. The game's sound is mixed in C# and queued here (audioPush), through an AnalyserNode
// to the speakers.
function setUpAudio() {
  const Context = globalThis.AudioContext ?? globalThis.webkitAudioContext;
  if (!Context)
    return;
  let context;
  try {
    context = new Context({ sampleRate: SAMPLE_RATE, latencyHint: 'interactive' });
  } catch {
    try {
      context = new Context();
    } catch {
      return;
    }
  }

  const analyser = context.createAnalyser();
  analyser.fftSize = 2048;
  analyser.connect(context.destination);
  audio = { context, analyser, next: 0, samples: new Float32Array(analyser.fftSize) };
  const resume = () => {
    if (context.state !== 'running')
      context.resume().catch(() => {});
  };
  for (const type of ['pointerdown', 'keydown', 'touchend'])
    addEventListener(type, resume, { capture: true });
  context.addEventListener('statechange', () => {
    if (context.state === 'running')
      hint.hidden = true;
  });
}

// Fullscreen the game asked for: now, or with the next click or key press if the browser wouldn't allow it without one.
function applyFullscreen() {
  if (wantFullscreen && !document.fullscreenElement)
    document.documentElement.requestFullscreen?.().catch(() => {});
  else if (!wantFullscreen && document.fullscreenElement)
    document.exitFullscreen?.().catch(() => {});
}

function textureOf(id) {
  return textures.get(id) ?? (id > 0 ? missing : white);
}

// Drawing goes only into the view (which WebGL counts from the bottom left).
function clip(left, top, width, height, targetHeight) {
  gl.scissor(left, targetHeight - top - height, Math.max(width, 0), Math.max(height, 0));
}


// A picture the size of the canvas to draw into (the light map, with a stencil buffer for shadows, or the picture the
// screen effects are drawn from), made again when the canvas's size changes.
function ensureTarget(target, width, height, stencil) {
  if (target && target.width === width && target.height === height)
    return target;
  if (target) {
    gl.deleteFramebuffer(target.framebuffer);
    gl.deleteTexture(target.texture);
    if (target.stencil)
      gl.deleteRenderbuffer(target.stencil);
  }

  const made = { width, height, texture: gl.createTexture(), framebuffer: gl.createFramebuffer(), stencil: null };
  gl.bindTexture(gl.TEXTURE_2D, made.texture);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
  filter(gl.LINEAR);
  gl.bindFramebuffer(gl.FRAMEBUFFER, made.framebuffer);
  gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, made.texture, 0);
  if (stencil) {
    made.stencil = gl.createRenderbuffer();
    gl.bindRenderbuffer(gl.RENDERBUFFER, made.stencil);
    gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH24_STENCIL8, width, height);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_STENCIL_ATTACHMENT, gl.RENDERBUFFER, made.stencil);
  }

  return made;
}

function setUp(header, vertex, sprite, shape, effectsSource) {
  const program = fragment => link(header + vertex, header + fragment);
  spriteProgram = program(sprite);
  gl.useProgram(spriteProgram);
  gl.uniform1i(gl.getUniformLocation(spriteProgram, 'uTexture'), 0);
  shapeProgram = program(shape);
  shapeMode = gl.getUniformLocation(shapeProgram, 'uMode');
  effectsProgram = program(effectsSource);
  gl.useProgram(effectsProgram);
  gl.uniform1i(gl.getUniformLocation(effectsProgram, 'uTexture'), 0);
  effects = {};
  for (const name of [...LOOK, 'uSize', 'uTint', 'uGrain', 'uPalette', 'uTime'])
    effects[name] = gl.getUniformLocation(effectsProgram, name);

  // Every attribute is the instance's: a quad's four corners all read the same one.
  const quadArray = () => {
    const array = gl.createVertexArray();
    gl.bindVertexArray(array);
    const data = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, data);
    for (let attribute = 0; attribute < 5; attribute++) {
      gl.enableVertexAttribArray(attribute);
      gl.vertexAttribPointer(attribute, 4, gl.FLOAT, false, QUAD_BYTES, attribute * 16);
      gl.vertexAttribDivisor(attribute, 1);
    }

    return [array, data];
  };
  [vao, buffer] = quadArray();
  [wholeVao, wholeBuffer] = quadArray();
  gl.bindVertexArray(null);
  white = makeTexture(new Uint8Array([255, 255, 255, 255]), 1, 1);
  missing = makeTexture(new Uint8Array([255, 0, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 0, 255, 255]), 2, 2);
}

function link(vertexSource, fragmentSource) {
  const compile = (type, source) => {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS))
      throw new Error(`A shader failed to compile: ${gl.getShaderInfoLog(shader)}`);
    return shader;
  };
  const linked = gl.createProgram();
  gl.attachShader(linked, compile(gl.VERTEX_SHADER, vertexSource));
  gl.attachShader(linked, compile(gl.FRAGMENT_SHADER, fragmentSource));
  gl.linkProgram(linked);
  if (!gl.getProgramParameter(linked, gl.LINK_STATUS))
    throw new Error(`A shader failed to link: ${gl.getProgramInfoLog(linked)}`);
  return linked;
}

function makeTexture(rgba, width, height) {
  const texture = gl.createTexture();
  upload(texture, rgba, width, height);
  return texture;
}

// Pixel art is the common jam style, so images sample with nearest-neighbour filtering, as on the desktop.
function upload(texture, source, width, height) {
  gl.bindTexture(gl.TEXTURE_2D, texture);
  unpack();
  if (source instanceof Uint8Array)
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, source);
  else
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, source);
  filter(gl.NEAREST);
}

function unpack() {
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
  gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);
}

function filter(mode) {
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, mode);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, mode);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
}

// Keys are the game's by where they are (KeyboardEvent.code), as on the desktop. The page keeps the ones the game takes,
// so the arrows and Space don't scroll it, but leaves the browser its own: F keys and shortcuts with Ctrl or Cmd.
function listen() {
  const keep = e => !e.ctrlKey && !e.metaKey && !/^F\d+$/.test(e.code);
  // A click or key press lets the page go fullscreen, if the game asked to before there was one.
  const gesture = () => {
    if (wantFullscreen && !document.fullscreenElement)
      applyFullscreen();
  };
  addEventListener('pointerdown', gesture, { capture: true });
  addEventListener('keydown', gesture, { capture: true });
  document.addEventListener('fullscreenchange', () => {
    wantFullscreen = !!document.fullscreenElement;
    player.FullscreenChanged(wantFullscreen);
  });
  document.addEventListener('pointerlockchange', () => player.MouseLocked(document.pointerLockElement === canvas));
  noticeBox.addEventListener('click', () => { noticeBox.hidden = true; });
  // The browser can take WebGL away (a GPU reset, too many pages drawing): the game can't go on drawing, so it says so.
  canvas.addEventListener('webglcontextlost', e => {
    e.preventDefault();
    fail("The browser stopped the game's graphics. Reload the page to play again.");
  });

  addEventListener('keydown', e => {
    if (player.Key(e.code, true, e.repeat) && keep(e))
      e.preventDefault();
    if (e.key === 'Backspace')
      player.Type('\b');
    else if (e.key.length === 1 && !e.ctrlKey && !e.metaKey)
      player.Type(e.key);
  });
  addEventListener('keyup', e => {
    if (player.Key(e.code, false, false) && keep(e))
      e.preventDefault();
  });
  addEventListener('blur', () => player.Blur());

  // The mouse, and touch as the mouse: one finger points and its tap is a left click (a second finger isn't the mouse).
  // Every finger is also a touch of its own, so several can be down at once (rhythm lanes, on-screen pads).
  const point = e => {
    const box = canvas.getBoundingClientRect();
    const scale = devicePixelRatio || 1;
    player.Pointer((e.clientX - box.left) / box.width, (e.clientY - box.top) / box.height, e.movementX * scale, e.movementY * scale);
  };
  const touch = (e, down) => {
    if (e.pointerType !== 'touch')
      return;
    const box = canvas.getBoundingClientRect();
    player.Touch(e.pointerId, (e.clientX - box.left) / box.width, (e.clientY - box.top) / box.height, down);
  };
  canvas.addEventListener('pointermove', e => {
    if (e.buttons)
      touch(e, true);
    if (e.isPrimary)
      point(e);
  });
  canvas.addEventListener('pointerleave', e => {
    if (!e.isPrimary)
      return;
    if (e.pointerType === 'mouse')
      player.PointerLeft();
    else
      touchLeft = true;
  });
  canvas.addEventListener('pointerdown', e => {
    canvas.focus();
    touch(e, true);
    if (!e.isPrimary) {
      if (e.pointerType === 'touch') {
        try {
          canvas.setPointerCapture(e.pointerId);
        } catch {
          // A finger already lifted can't be followed off the canvas: it isn't down any more.
        }
      }
      return;
    }
    // A first-person game takes the mouse when it's clicked; that click only takes it, as on the desktop.
    if (e.pointerType === 'mouse' && document.pointerLockElement !== canvas && player.WantsMouseLock() && canvas.requestPointerLock) {
      const asked = canvas.requestPointerLock();
      asked?.catch?.(() => {});
      return;
    }

    touchLeft = false;
    point(e);
    canvas.setPointerCapture(e.pointerId);
    player.Button(e.pointerType === 'mouse' ? e.button : 0, true);
  });
  canvas.addEventListener('pointerup', e => {
    touch(e, false);
    if (e.isPrimary)
      player.Button(e.pointerType === 'mouse' ? e.button : 0, false);
  });
  canvas.addEventListener('pointercancel', e => {
    touch(e, false);
    if (e.isPrimary)
      player.Button(0, false);
  });
  canvas.addEventListener('contextmenu', e => e.preventDefault());
  // A notch is 100 pixels of scrolling in Chrome and Edge, or 3 lines; more than 0 is away from the player, as on Windows.
  canvas.addEventListener('wheel', e => {
    player.Wheel(-e.deltaY / (e.deltaMode === 0 ? 100 : e.deltaMode === 1 ? 3 : 1));
    e.preventDefault();
  }, { passive: false });
}
