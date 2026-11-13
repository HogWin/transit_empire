using System;
using System.Collections.Generic;
using Godot;
using TransitEmpire.Core.Simulation;

namespace TransitEmpire.Client.Scripts;

/// <summary>
/// Gráfico evolutivo vetorial (barras + linha de lucro) dos últimos 12 meses na aba Banco & DRE.
/// </summary>
public partial class DreChartControl : Control
{
    private sealed record BarPoint(string Label, long Revenue, long Expenses, long CapEx, long NetProfit);

    private readonly List<BarPoint> _points = new();

    public DreChartControl()
    {
        CustomMinimumSize = new Vector2(360, 118);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    public void UpdateSeries(IReadOnlyList<FinancialStatement> history, FinancialStatement currentMonth, int curMonth, int curYear)
    {
        _points.Clear();

        int historyTake = Math.Min(11, history.Count);
        int startIdx = history.Count - historyTake;
        for (int i = startIdx; i < history.Count; i++)
        {
            var s = history[i];
            _points.Add(new BarPoint($"{s.Month:D2}", s.TotalRevenue, s.TotalOperatingExpenses, s.InfrastructureAndFleetExpense, s.OperatingProfit));
        }

        _points.Add(new BarPoint($"{curMonth:D2}*", currentMonth.TotalRevenue, currentMonth.TotalOperatingExpenses, currentMonth.InfrastructureAndFleetExpense, currentMonth.OperatingProfit));
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        if (size.X < 40 || size.Y < 40)
            return;

        DrawRect(new Rect2(Vector2.Zero, size), new Color("e2e8f0"));
        DrawRect(new Rect2(1, 1, size.X - 2, size.Y - 2), new Color("f8fafc"));

        Font font = ThemeDB.FallbackFont;
        int fontSize = 9;

        // Legenda compacta no topo
        DrawLegendChip(font, fontSize, 8, 12, new Color("16a34a"), "Receita");
        DrawLegendChip(font, fontSize, 74, 12, new Color("dc2626"), "Custos");
        DrawLegendChip(font, fontSize, 134, 12, new Color("d97706"), "CapEx");
        DrawLegendChip(font, fontSize, 192, 12, new Color("0284c7"), "Lucro Op.");

        if (_points.Count == 0)
            return;

        float padLeft = 10f;
        float padRight = 10f;
        float padTop = 22f;
        float padBottom = 18f;
        float plotW = Math.Max(10f, size.X - padLeft - padRight);
        float plotH = Math.Max(10f, size.Y - padTop - padBottom);

        long maxVal = 1000;
        long minVal = 0;
        foreach (var pt in _points)
        {
            maxVal = Math.Max(maxVal, Math.Max(pt.Revenue, Math.Max(pt.Expenses, pt.CapEx)));
            minVal = Math.Min(minVal, pt.NetProfit);
        }

        long range = Math.Max(100L, maxVal - minVal);
        float zeroY = padTop + ((float)maxVal / range) * plotH;

        // Linhas horizontais guia + linha base zero
        DrawLine(new Vector2(padLeft, padTop), new Vector2(size.X - padRight, padTop), new Color("e2e8f0"), 1f);
        DrawLine(new Vector2(padLeft, zeroY), new Vector2(size.X - padRight, zeroY), new Color("94a3b8"), 1.2f);

        int slots = Math.Max(1, _points.Count);
        float slotW = plotW / slots;
        float barW = Math.Clamp((slotW - 6f) / 3f, 2f, 10f);

        Vector2? prevProfitPt = null;
        for (int i = 0; i < _points.Count; i++)
        {
            var pt = _points[i];
            float cx = padLeft + i * slotW + slotW * 0.5f;

            DrawBar(cx - barW * 1.55f, zeroY, barW, pt.Revenue, range, plotH, new Color("16a34a"));
            DrawBar(cx - barW * 0.50f, zeroY, barW, pt.Expenses, range, plotH, new Color("dc2626"));
            DrawBar(cx + barW * 0.55f, zeroY, barW, pt.CapEx, range, plotH, new Color("d97706"));

            float profitY = zeroY - ((float)pt.NetProfit / range) * plotH;
            profitY = Math.Clamp(profitY, padTop, padTop + plotH);
            var curPt = new Vector2(cx, profitY);

            if (prevProfitPt.HasValue)
            {
                DrawLine(prevProfitPt.Value, curPt, new Color("0284c7"), 2.0f, antialiased: true);
            }
            DrawCircle(curPt, 2.8f, new Color("0284c7"));
            prevProfitPt = curPt;

            DrawString(font, new Vector2(cx - 8f, size.Y - 4f), pt.Label, HorizontalAlignment.Left, -1, fontSize, new Color("475569"));
        }
    }

    private void DrawBar(float x, float zeroY, float width, long val, long range, float plotH, Color color)
    {
        if (val <= 0) return;
        float h = Math.Max(1.5f, ((float)val / range) * plotH);
        DrawRect(new Rect2(x, zeroY - h, width, h), color);
    }

    private void DrawLegendChip(Font font, int fontSize, float x, float y, Color color, string text)
    {
        DrawRect(new Rect2(x, y - 7f, 8f, 8f), color);
        DrawString(font, new Vector2(x + 11f, y), text, HorizontalAlignment.Left, -1, fontSize, new Color("334155"));
    }
}

