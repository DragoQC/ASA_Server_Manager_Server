(function () {
    const canvas = document.getElementById("neon-network-bg");
    if (!canvas) {
        return;
    }

    const context = canvas.getContext("2d");
    if (!context) {
        return;
    }

    const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const isCoarsePointer = window.matchMedia("(pointer: coarse), (hover: none)").matches;
    const mouse = {
        x: 0,
        y: 0,
        active: false,
        lastMoveTime: 0
    };

    let animationFrameId = 0;
    let particles = [];
    let viewportWidth = 0;
    let viewportHeight = 0;
    let deviceScale = 1;
    let lastFrameTime = performance.now();
    let accumulator = 0;
    let palette = readPalette();

    const targetFps = prefersReducedMotion ? 12 : isCoarsePointer ? 18 : 24;
    const frameInterval = 1000 / targetFps;
    const interactiveMouseDurationMs = 1400;

    function clamp(value, min, max) {
        return Math.max(min, Math.min(max, value));
    }

    function readPalette() {
        const styles = window.getComputedStyle(document.documentElement);
        const rawHue = Number.parseFloat(styles.getPropertyValue("--theme-hue"));
        const hue = Number.isFinite(rawHue) ? rawHue : 191;

        return {
            lineHue: hue + 10,
            mouseHue: hue - 6,
            particleHue: hue - 12,
            shadowHue: hue + 4
        };
    }

    function getViewportSize() {
        const width = window.innerWidth;
        const screenHeight = Math.max(window.screen.height || 0, window.screen.availHeight || 0);
        const height = isCoarsePointer
            ? Math.max(window.innerHeight, screenHeight)
            : window.innerHeight;
        return { width, height };
    }

    function resize(force) {
        const nextViewport = getViewportSize();

        if (!force && isCoarsePointer) {
            const widthChanged = nextViewport.width !== viewportWidth;
            const heightDelta = Math.abs(nextViewport.height - viewportHeight);

            if (!widthChanged && heightDelta < 160) {
                return;
            }
        }

        viewportWidth = nextViewport.width;
        viewportHeight = nextViewport.height;
        deviceScale = Math.min(window.devicePixelRatio || 1, isCoarsePointer ? 1 : 1.25);

        canvas.width = Math.floor(viewportWidth * deviceScale);
        canvas.height = Math.floor(viewportHeight * deviceScale);
        canvas.style.width = `${viewportWidth}px`;
        canvas.style.height = `${viewportHeight}px`;

        context.setTransform(deviceScale, 0, 0, deviceScale, 0, 0);
        createParticles();
        drawFrame(0);
    }

    function createParticles() {
        const particleCount = isCoarsePointer
            ? clamp(Math.floor((viewportWidth * viewportHeight) / 36000), 24, 44)
            : clamp(Math.floor((viewportWidth * viewportHeight) / 26000), 32, 72);

        particles = Array.from({ length: particleCount }, () => ({
            x: Math.random() * viewportWidth,
            y: Math.random() * viewportHeight,
            vx: (Math.random() - 0.5) * (prefersReducedMotion ? 0.08 : isCoarsePointer ? 0.16 : 0.3),
            vy: (Math.random() - 0.5) * (prefersReducedMotion ? 0.08 : isCoarsePointer ? 0.16 : 0.3),
            radius: 1 + Math.random() * 2.4
        }));
    }

    function updateParticles(deltaSeconds) {
        for (const particle of particles) {
            particle.x += particle.vx * deltaSeconds * 60;
            particle.y += particle.vy * deltaSeconds * 60;

            if (particle.x <= 0 || particle.x >= viewportWidth) {
                particle.vx *= -1;
                particle.x = clamp(particle.x, 0, viewportWidth);
            }

            if (particle.y <= 0 || particle.y >= viewportHeight) {
                particle.vy *= -1;
                particle.y = clamp(particle.y, 0, viewportHeight);
            }
        }
    }

    function drawFrame(deltaSeconds) {
        updateParticles(deltaSeconds);

        context.clearRect(0, 0, viewportWidth, viewportHeight);

        const connectionDistance = isCoarsePointer
            ? Math.min(170, Math.max(110, viewportWidth * 0.105))
            : Math.min(210, Math.max(130, viewportWidth * 0.125));
        const connectionDistanceSquared = connectionDistance * connectionDistance;
        const mouseDistance = 180;
        const mouseDistanceSquared = mouseDistance * mouseDistance;
        const mouseIsInteractive = !isCoarsePointer &&
            mouse.active &&
            performance.now() - mouse.lastMoveTime <= interactiveMouseDurationMs;

        const farLinePath = new Path2D();
        const midLinePath = new Path2D();
        const nearLinePath = new Path2D();
        const mouseLinePath = mouseIsInteractive ? new Path2D() : null;
        const particlePath = new Path2D();

        for (let i = 0; i < particles.length; i++) {
            const particle = particles[i];

            for (let j = i + 1; j < particles.length; j++) {
                const otherParticle = particles[j];
                const dx = otherParticle.x - particle.x;
                const dy = otherParticle.y - particle.y;
                const distanceSquared = dx * dx + dy * dy;

                if (distanceSquared > connectionDistanceSquared) {
                    continue;
                }

                const distanceRatio = distanceSquared / connectionDistanceSquared;
                const targetPath = distanceRatio < 0.18
                    ? nearLinePath
                    : distanceRatio < 0.48
                        ? midLinePath
                        : farLinePath;

                targetPath.moveTo(particle.x, particle.y);
                targetPath.lineTo(otherParticle.x, otherParticle.y);
            }

            let glowBoost = 0;

            if (mouseIsInteractive && mouseLinePath) {
                const mouseDx = mouse.x - particle.x;
                const mouseDy = mouse.y - particle.y;
                const mouseRangeSquared = mouseDx * mouseDx + mouseDy * mouseDy;

                if (mouseRangeSquared <= mouseDistanceSquared) {
                    const alpha = 1 - mouseRangeSquared / mouseDistanceSquared;
                    glowBoost = alpha * 0.8;
                    mouseLinePath.moveTo(mouse.x, mouse.y);
                    mouseLinePath.lineTo(particle.x, particle.y);
                }
            }

            particlePath.moveTo(particle.x + particle.radius + glowBoost * 0.5, particle.y);
            particlePath.arc(particle.x, particle.y, particle.radius + glowBoost * 0.5, 0, Math.PI * 2);
        }

        context.lineWidth = 1;
        context.strokeStyle = `hsla(${palette.lineHue}, 100%, 68%, 0.08)`;
        context.stroke(farLinePath);
        context.strokeStyle = `hsla(${palette.lineHue}, 100%, 68%, 0.14)`;
        context.stroke(midLinePath);
        context.strokeStyle = `hsla(${palette.lineHue}, 100%, 68%, 0.22)`;
        context.stroke(nearLinePath);

        if (mouseLinePath) {
            context.lineWidth = 1.2;
            context.strokeStyle = `hsla(${palette.mouseHue}, 100%, 78%, 0.24)`;
            context.stroke(mouseLinePath);
        }

        context.fillStyle = `hsla(${palette.particleHue}, 100%, 84%, 0.78)`;
        context.shadowBlur = mouseIsInteractive ? 18 : 12;
        context.shadowColor = `hsla(${palette.shadowHue}, 100%, 62%, 0.75)`;
        context.fill(particlePath);
        context.shadowBlur = 0;
    }

    function tick(now) {
        const elapsedMs = Math.min(now - lastFrameTime, 250);
        lastFrameTime = now;
        accumulator += elapsedMs;

        if (accumulator < frameInterval) {
            animationFrameId = window.requestAnimationFrame(tick);
            return;
        }

        const deltaSeconds = prefersReducedMotion ? 0 : Math.min(accumulator / 1000, 0.05);
        accumulator = 0;
        drawFrame(deltaSeconds);
        animationFrameId = window.requestAnimationFrame(tick);
    }

    function handleMouseMove(event) {
        mouse.x = event.clientX;
        mouse.y = event.clientY;
        mouse.active = true;
        mouse.lastMoveTime = performance.now();
    }

    function handleMouseLeave() {
        mouse.active = false;
    }

    window.addEventListener("resize", function () { resize(false); });
    window.addEventListener("orientationchange", function () { resize(true); });

    if (!isCoarsePointer) {
        window.addEventListener("mousemove", handleMouseMove, { passive: true });
        window.addEventListener("mouseleave", handleMouseLeave, { passive: true });
        window.addEventListener("blur", handleMouseLeave, { passive: true });
    }

    window.addEventListener("asa-theme-change", function () {
        palette = readPalette();
        drawFrame(0);
    });

    resize(true);
    animationFrameId = window.requestAnimationFrame(tick);

    document.addEventListener("visibilitychange", function () {
        if (document.hidden) {
            mouse.active = false;
            window.cancelAnimationFrame(animationFrameId);
            animationFrameId = 0;
            return;
        }

        if (animationFrameId === 0) {
            lastFrameTime = performance.now();
            accumulator = 0;
            animationFrameId = window.requestAnimationFrame(tick);
        }
    });
})();
