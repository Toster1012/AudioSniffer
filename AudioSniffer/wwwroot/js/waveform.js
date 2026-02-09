window.drawWaveform = function(canvas, dataJson) {
    const data = JSON.parse(dataJson);
    const ctx = canvas.getContext('2d');

    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    ctx.scale(dpr, dpr);

    const width = rect.width;
    const height = rect.height;
    const centerY = height / 2;

    ctx.clearRect(0, 0, width, height);

    const isDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const barColor = isDark ? '#0077ed' : '#0071e3';

    const barWidth = Math.max(width / data.length, 1.5);
    const maxAmplitude = Math.max(...data.map(Math.abs));
    const gap = Math.max(barWidth * 0.3, 1);
    const actualBarWidth = Math.max(barWidth - gap, 1);

    for (let i = 0; i < data.length; i++) {
        const x = i * barWidth;
        const normalizedValue = data[i] / maxAmplitude;
        const barHeight = Math.abs(normalizedValue) * (height / 2) * 0.85;

        ctx.fillStyle = barColor;

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

        ctx.fillStyle = backgroundColor;
        ctx.fillRect(0, 0, width, height);

        const barWidth = Math.max(width / data.length, 1.5);
        const maxAmplitude = Math.max(...data.map(Math.abs));
        const gap = Math.max(barWidth * 0.3, 1);
        const actualBarWidth = Math.max(barWidth - gap, 1);

        for (let i = 0; i < data.length; i++) {
            const x = i * barWidth;

            const phase = (frame * 0.05 + i * 0.15) % (Math.PI * 2);
            const waveAnimation = Math.sin(phase) * 0.25 + 1;

            const pulsePhase = (frame * 0.03) % (Math.PI * 2);
            const pulseAnimation = Math.sin(pulsePhase) * 0.15 + 1;

            const normalizedValue = (data[i] / maxAmplitude) * waveAnimation * pulseAnimation;
            const barHeight = Math.abs(normalizedValue) * (height / 2) * 0.85;

            const gradient = ctx.createLinearGradient(x, centerY - barHeight, x, centerY + barHeight);
            gradient.addColorStop(0, barColor);
            gradient.addColorStop(1, isDark ? 'rgba(0, 119, 237, 0.6)' : 'rgba(0, 113, 227, 0.6)');
            ctx.fillStyle = gradient;

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

        requestAnimationFrame(animate);
    }

    animate();
}

window.createComparisonChart = function(canvas, dates, generatedCounts, realCounts) {
    const ctx = canvas.getContext('2d');

    if (canvas.chart) {
        canvas.chart.destroy();
    }

    const isDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const backgroundColor = isDark ? '#1d1d1f' : '#ffffff';
    const gridColor = isDark ? '#3a3a3c' : '#e5e5e5';
    const textColor = isDark ? '#ffffff' : '#333333';
    const generatedColor = 'rgba(255, 99, 132, 0.7)';
    const realColor = 'rgba(54, 162, 235, 0.7)';

    canvas.chart = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: dates,
            datasets: [
                {
                    label: 'Сгенерировано ИИ',
                    data: generatedCounts,
                    backgroundColor: generatedColor,
                    borderColor: 'rgba(255, 99, 132, 1)',
                    borderWidth: 1,
                    borderRadius: 4,
                    borderSkipped: false
                },
                {
                    label: 'Реальные',
                    data: realCounts,
                    backgroundColor: realColor,
                    borderColor: 'rgba(54, 162, 235, 1)',
                    borderWidth: 1,
                    borderRadius: 4,
                    borderSkipped: false
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            scales: {
                x: {
                    stacked: false,
                    grid: {
                        color: gridColor
                    },
                    ticks: {
                        color: textColor,
                        maxRotation: 45,
                        minRotation: 45
                    }
                },
                y: {
                    beginAtZero: true,
                    stacked: false,
                    grid: {
                        color: gridColor
                    },
                    ticks: {
                        color: textColor,
                        stepSize: 1
                    }
                }
            },
            plugins: {
                legend: {
                    position: 'top',
                    labels: {
                        color: textColor,
                        font: {
                            size: 14,
                            weight: 'bold'
                        },
                        padding: 15
                    }
                },
                tooltip: {
                    backgroundColor: isDark ? 'rgba(0, 0, 0, 0.8)' : 'rgba(255, 255, 255, 0.9)',
                    titleColor: isDark ? '#ffffff' : '#333333',
                    bodyColor: isDark ? '#ffffff' : '#333333',
                    borderColor: isDark ? '#444444' : '#cccccc',
                    borderWidth: 1,
                    padding: 12,
                    cornerRadius: 6,
                    callbacks: {
                        label: function(context) {
                            return context.dataset.label + ': ' + context.raw + ' файлов';
                        }
                    }
                }
            },
            animation: {
                duration: 1000,
                easing: 'easeInOutQuart'
            }
        }
    });
};
