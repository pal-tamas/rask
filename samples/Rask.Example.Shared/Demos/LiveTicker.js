// Scoped-JS bundle entry for the LiveTicker component. Wrapped at build time
// as `window.Rask.LiveTicker = { draw }` by the source generator (auto-globbed
// by Rask.Core/build/Rask.Core.targets next to LiveTicker.cs).
//
// draw(history): renders or updates a Chart.js Line chart on the
// [data-rask-ticker] canvas. The Chart instance is stored on the canvas
// (__raskChart) so subsequent draws update the existing chart instead of
// recreating it — `chart.update("none")` keeps the refresh jitter-free.
//
// `history` is an array of { timestamp, priceUsd } objects. The framework's
// IJSRuntime serializer camelCases public property names when marshalling
// C# records over JSInterop — so the C# PricePoint(Timestamp, PriceUsd)
// arrives in JS as { timestamp, priceUsd }. Reading p.Timestamp here would
// surface as `Invalid Date` on the chart axis.

export function draw(history) {
    const canvas = document.querySelector("[data-rask-ticker]");
    if (!canvas || typeof window.Chart !== "function") return;

    const labels = (history || []).map(p => formatTime(p.timestamp));
    const data = (history || []).map(p => Number(p.priceUsd));

    if (canvas.__raskChart) {
        canvas.__raskChart.data.labels = labels;
        canvas.__raskChart.data.datasets[0].data = data;
        // "none" — no animation between updates so the live refresh doesn't
        // tween from old values to new ones each poll.
        canvas.__raskChart.update("none");
        return;
    }

    canvas.__raskChart = new window.Chart(canvas.getContext("2d"), {
        type: "line",
        data: {
            labels,
            datasets: [{
                data,
                borderColor: "#0d6efd",
                backgroundColor: "rgba(13, 110, 253, 0.15)",
                borderWidth: 2,
                tension: 0.25,
                pointRadius: 0,
                pointHoverRadius: 4,
                fill: true,
            }],
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            plugins: {
                legend: {display: false},
                tooltip: {
                    intersect: false,
                    mode: "index",
                    callbacks: {
                        label: ctx => "$" + Number(ctx.parsed.y).toLocaleString(undefined, {
                            minimumFractionDigits: 2,
                            maximumFractionDigits: 2,
                        }),
                    },
                },
            },
            scales: {
                x: {ticks: {maxTicksLimit: 6, color: "#6c757d"}, grid: {display: false}},
                y: {
                    ticks: {color: "#6c757d", callback: v => "$" + Number(v).toLocaleString()},
                    grid: {color: "rgba(0,0,0,0.05)"},
                },
            },
        },
    });
}

function formatTime(iso) {
    // ISO 8601 → HH:mm:ss in the viewer's locale.
    try {
        return new Date(iso).toLocaleTimeString();
    } catch (e) {
        return String(iso);
    }
}
