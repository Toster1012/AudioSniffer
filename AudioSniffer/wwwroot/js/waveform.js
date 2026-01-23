window.drawWaveform = function(canvas, dataJson) {
    const data = JSON.parse(dataJson);
    const ctx = canvas.getContext('2d');
    
    // Set canvas size
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    ctx.scale(dpr, dpr);
    
    const width = rect.width;
    const height = rect.height;
    const centerY = height / 2;
    
    // Clear canvas
    ctx.clearRect(0, 0, width, height);
    
    // Get color
    const isDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const barColor = isDark ? '#0077ed' : '#0071e3';
    
    // Draw waveform with thinner bars
    const barWidth = Math.max(width / data.length, 1.5);
    const maxAmplitude = Math.max(...data.map(Math.abs));
    const gap = Math.max(barWidth * 0.3, 1);
    const actualBarWidth = Math.max(barWidth - gap, 1);
    
    for (let i = 0; i < data.length; i++) {
        const x = i * barWidth;
        const normalizedValue = data[i] / maxAmplitude;
        const barHeight = Math.abs(normalizedValue) * (height / 2) * 0.85;
        
        ctx.fillStyle = barColor;
        
        // Draw thin bar with rounded corners
        const barX = x + gap / 2;
        const barW = actualBarWidth;
        const radius = Math.min(barW / 2, 1.5);
        
        if (normalizedValue >= 0) {
            roundRect(ctx, barX, centerY - barHeight, barW, barHeight, radius);
        } else {
            roundRect(ctx, barX, centerY, barW, barHeight, radius);
        }
        ctx.fill();
    }
    
    // Animate waveform
    animateWaveform(canvas, data);
};

function roundRect(ctx, x, y, width, height, radius) {
    if (height < radius * 2) {
        radius = height / 2;
    }
    
    ctx.beginPath();
    ctx.moveTo(x + radius, y);
    ctx.lineTo(x + width - radius, y);
    ctx.quadraticCurveTo(x + width, y, x + width, y + radius);
    ctx.lineTo(x + width, y + height - radius);
    ctx.quadraticCurveTo(x + width, y + height, x + width - radius, y + height);
    ctx.lineTo(x + radius, y + height);
    ctx.quadraticCurveTo(x, y + height, x, y + height - radius);
    ctx.lineTo(x, y + radius);
    ctx.quadraticCurveTo(x, y, x + radius, y);
    ctx.closePath();
}

function animateWaveform(canvas, data) {
    const ctx = canvas.getContext('2d');
    const rect = canvas.getBoundingClientRect();
    const width = rect.width;
    const height = rect.height;
    const centerY = height / 2;
    let frame = 0;
    
    const isDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const barColor = isDark ? '#0077ed' : '#0071e3';
    const backgroundColor = isDark ? 'rgba(29, 29, 31, 0.1)' : 'rgba(255, 255, 255, 0.1)';
    
    function animate() {
        if (!canvas.isConnected) return;
        
        frame++;
        
        // Clear with fade effect
        ctx.fillStyle = backgroundColor;
        ctx.fillRect(0, 0, width, height);
        
        // Draw animated waveform
        const barWidth = Math.max(width / data.length, 1.5);
        const maxAmplitude = Math.max(...data.map(Math.abs));
        const gap = Math.max(barWidth * 0.3, 1);
        const actualBarWidth = Math.max(barWidth - gap, 1);
        
        for (let i = 0; i < data.length; i++) {
            const x = i * barWidth;
            
            // Более выраженная волновая анимация
            const phase = (frame * 0.05 + i * 0.15) % (Math.PI * 2);
            const waveAnimation = Math.sin(phase) * 0.25 + 1;
            
            // Добавляем пульсацию
            const pulsePhase = (frame * 0.03) % (Math.PI * 2);
            const pulseAnimation = Math.sin(pulsePhase) * 0.15 + 1;
            
            const normalizedValue = (data[i] / maxAmplitude) * waveAnimation * pulseAnimation;
            const barHeight = Math.abs(normalizedValue) * (height / 2) * 0.85;
            
            // Градиент для более красивого эффекта
            const gradient = ctx.createLinearGradient(x, centerY - barHeight, x, centerY + barHeight);
            gradient.addColorStop(0, barColor);
            gradient.addColorStop(1, isDark ? 'rgba(0, 119, 237, 0.6)' : 'rgba(0, 113, 227, 0.6)');
            ctx.fillStyle = gradient;
            
            // Draw thin bar with rounded corners
            const barX = x + gap / 2;
            const barW = actualBarWidth;
            const radius = Math.min(barW / 2, 1.5);
            
            if (normalizedValue >= 0) {
                roundRect(ctx, barX, centerY - barHeight, barW, barHeight, radius);
            } else {
                roundRect(ctx, barX, centerY, barW, barHeight, radius);
            }
            ctx.fill();
        }
        
        // Продолжаем анимацию бесконечно
        requestAnimationFrame(animate);
    }
    
    animate();
}