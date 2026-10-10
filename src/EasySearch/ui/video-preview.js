// One selected video, five small decoded frames. No persistent video/frame cache.
class VideoPreview {
  constructor(post) {
    this.post = post;
    this.sequence = 0;
    this.active = null;
  }

  clear() {
    const active = this.active;
    if (!active) return;
    this.active = null;
    active.abort.abort();
    clearTimeout(active.debounce);
    clearInterval(active.loop);
    this.releaseVideo(active);
    this.post({ type: 'videoPreview', id: active.id, path: null });
    for (const canvas of active.frames) canvas.width = canvas.height = 0;
    active.frames.length = 0;
    if (active.display) active.display.width = active.display.height = 0;
  }

  select(path, hero) {
    this.clear();
    const active = this.active = {
      id: ++this.sequence, hero, abort: new AbortController(), frames: [], video: null,
    };
    // Arrow-key navigation should never open a decoder for every passing result.
    active.debounce = setTimeout(() => {
      if (this.active === active) this.post({ type: 'videoPreview', id: active.id, path });
    }, 120);
  }

  source(message) {
    const active = this.active;
    if (!active || message.id !== active.id) return;
    this.load(active, message.url).catch(() => {
      // Unsupported codecs, corrupt files and timeouts keep the Windows thumbnail.
      if (this.active === active) this.clear();
    });
  }

  releaseVideo(active) {
    const video = active.video;
    if (!video) return;
    active.video = null;
    video.pause();
    video.removeAttribute('src');
    video.load(); // closes the media source and releases decoder/buffers
    video.remove();
  }

  wait(video, event, signal, begin = () => {}) {
    return new Promise((resolve, reject) => {
      const finish = error => {
        clearTimeout(timeout);
        video.removeEventListener(event, ready);
        video.removeEventListener('error', failed);
        signal.removeEventListener('abort', aborted);
        error ? reject(error) : resolve();
      };
      const ready = () => finish();
      const failed = () => finish(new Error('Video unavailable'));
      const aborted = () => finish(new Error('Preview cancelled'));
      const timeout = setTimeout(failed, 8000);
      video.addEventListener(event, ready, { once: true });
      video.addEventListener('error', failed, { once: true });
      signal.addEventListener('abort', aborted, { once: true });
      if (signal.aborted) return aborted();
      try { begin(); } catch (error) { finish(error); }
    });
  }

  async load(active, url) {
    const video = active.video = document.createElement('video');
    const signal = active.abort.signal;
    video.muted = true;
    video.crossOrigin = 'anonymous';
    video.preload = 'auto';
    video.playsInline = true;
    // An overall deadline also covers a stream that stalls between seeks.
    const deadline = setTimeout(() => {
      if (this.active === active) this.clear();
    }, 20000);
    try {
      await this.wait(video, 'loadeddata', signal, () => { video.src = url; });
      if (!Number.isFinite(video.duration) || video.duration <= 0 || !video.videoWidth || !video.videoHeight)
        throw new Error('No seekable video');
      const scale = Math.min(1, 512 / Math.max(video.videoWidth, video.videoHeight));
      const width = Math.max(1, Math.round(video.videoWidth * scale));
      const height = Math.max(1, Math.round(video.videoHeight * scale));
      for (let index = 0; index < 5; index++) {
        signal.throwIfAborted();
        const time = video.duration * index / 5;
        if (video.currentTime !== time)
          await this.wait(video, 'seeked', signal, () => { video.currentTime = time; });
        signal.throwIfAborted();
        const frame = document.createElement('canvas');
        frame.width = width;
        frame.height = height;
        active.frames.push(frame);
        frame.getContext('2d', { alpha: false }).drawImage(video, 0, 0, width, height);
      }
      this.releaseVideo(active);
      this.post({ type: 'videoPreview', id: active.id, path: null });
      signal.throwIfAborted();
      const display = active.display = document.createElement('canvas');
      display.width = width;
      display.height = height;
      display.style.cssText = 'max-width:100%;max-height:150px;object-fit:contain';
      display.setAttribute('role', 'img');
      display.setAttribute('aria-label', 'Video preview: five frames at 20 percent intervals');
      const context = display.getContext('2d', { alpha: false });
      let index = 0;
      const draw = () => {
        context.drawImage(active.frames[index], 0, 0);
        index = (index + 1) % 5;
      };
      draw();
      active.hero.replaceChildren(display);
      active.loop = setInterval(draw, 500);
    } finally {
      clearTimeout(deadline);
      this.releaseVideo(active);
    }
  }
}
