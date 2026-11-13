using System;
using System.Collections.Generic;

namespace TransitEmpire.Core.Simulation;

/// <summary>
/// Demonstrativo de Resultado do Exercício (DRE) e Fluxo de Caixa de um período ou acumulado.
/// </summary>
public class FinancialStatement
{
    public int Month { get; set; }
    public int Year { get; set; }

    // Receitas Operacionais
    public long FreightRevenue { get; set; }
    public long TicketRevenue { get; set; }
    public long ContractBonusRevenue { get; set; }
    public long MunicipalSubsidyRevenue { get; set; }
    public long TollRevenue { get; set; }

    // Custos e Despesas Operacionais
    public long FleetMaintenanceExpense { get; set; }
    public long RoadConstructionExpense { get; set; }
    public long RoadMaintenanceExpense { get; set; }
    public long LoanInterestExpense { get; set; }

    // Investimentos (CapEx: Frota, Garagens, Hubs, P&D, Regiões)
    public long InfrastructureAndFleetExpense { get; set; }

    // Snapshot de caixa no fechamento
    public long ClosingBalance { get; set; }
    public long ClosingDebt { get; set; }

    public long TotalRevenue => FreightRevenue + TicketRevenue + ContractBonusRevenue + MunicipalSubsidyRevenue + TollRevenue;
    public long TotalOperatingExpenses => FleetMaintenanceExpense + RoadConstructionExpense + RoadMaintenanceExpense + LoanInterestExpense;
    public long OperatingProfit => TotalRevenue - TotalOperatingExpenses;
    public long NetCashFlow => TotalRevenue - TotalOperatingExpenses - InfrastructureAndFleetExpense;

    public FinancialStatement Clone(int month, int year, long balance, long debt)
    {
        return new FinancialStatement
        {
            Month = month,
            Year = year,
            FreightRevenue = FreightRevenue,
            TicketRevenue = TicketRevenue,
            ContractBonusRevenue = ContractBonusRevenue,
            MunicipalSubsidyRevenue = MunicipalSubsidyRevenue,
            TollRevenue = TollRevenue,
            FleetMaintenanceExpense = FleetMaintenanceExpense,
            RoadConstructionExpense = RoadConstructionExpense,
            RoadMaintenanceExpense = RoadMaintenanceExpense,
            LoanInterestExpense = LoanInterestExpense,
            InfrastructureAndFleetExpense = InfrastructureAndFleetExpense,
            ClosingBalance = balance,
            ClosingDebt = debt
        };
    }

    public void Reset()
    {
        FreightRevenue = 0;
        TicketRevenue = 0;
        ContractBonusRevenue = 0;
        MunicipalSubsidyRevenue = 0;
        TollRevenue = 0;
        FleetMaintenanceExpense = 0;
        RoadConstructionExpense = 0;
        RoadMaintenanceExpense = 0;
        LoanInterestExpense = 0;
        InfrastructureAndFleetExpense = 0;
    }
}

/// <summary>
/// Livro-razão financeiro que contabiliza a DRE do mês corrente, o acumulado geral e os fechamentos mensais.
/// </summary>
public class FinancialLedger
{
    public FinancialStatement CurrentMonth { get; } = new();
    public FinancialStatement Lifetime { get; } = new();
    public List<FinancialStatement> MonthlyHistory { get; } = new();

    public void RecordFreightRevenue(long amount)
    {
        if (amount <= 0) return;
        CurrentMonth.FreightRevenue += amount;
        Lifetime.FreightRevenue += amount;
    }

    public void RecordTicketRevenue(long amount)
    {
        if (amount <= 0) return;
        CurrentMonth.TicketRevenue += amount;
        Lifetime.TicketRevenue += amount;
    }

    public void RecordContractBonus(long amount)
    {
        if (amount <= 0) return;
        CurrentMonth.ContractBonusRevenue += amount;
        Lifetime.ContractBonusRevenue += amount;
    }

    public void RecordMunicipalSubsidy(long amount)
    {
        if (amount <= 0) return;
        CurrentMonth.MunicipalSubsidyRevenue += amount;
        Lifetime.MunicipalSubsidyRevenue += amount;
    }

    public void RecordTollRevenue(long amount)
    {
        if (amount <= 0) return;
        CurrentMonth.TollRevenue += amount;
        Lifetime.TollRevenue += amount;
    }

    public void RecordRoadMaintenance(long amount)
    {
        if (amount <= 0) return;
        CurrentMonth.RoadMaintenanceExpense += amount;
        Lifetime.RoadMaintenanceExpense += amount;
    }

    public void RecordFleetMaintenance(long amount)
    {
        if (amount <= 0) return;
        CurrentMonth.FleetMaintenanceExpense += amount;
        Lifetime.FleetMaintenanceExpense += amount;
    }

    public void RecordRoadConstruction(long amount)
    {
        if (amount <= 0) return;
        CurrentMonth.RoadConstructionExpense += amount;
        Lifetime.RoadConstructionExpense += amount;
    }

    public void RecordLoanInterest(long amount)
    {
        if (amount <= 0) return;
        CurrentMonth.LoanInterestExpense += amount;
        Lifetime.LoanInterestExpense += amount;
    }

    public void RecordCapEx(long amount)
    {
        if (amount <= 0) return;
        CurrentMonth.InfrastructureAndFleetExpense += amount;
        Lifetime.InfrastructureAndFleetExpense += amount;
    }

    /// <summary>
    /// Fecha a DRE do mês encerrado, arquiva no histórico (máx. 24 meses) e zera o mês corrente.
    /// </summary>
    public FinancialStatement CloseMonth(int month, int year, long currentBalance, long currentDebt)
    {
        var snapshot = CurrentMonth.Clone(month, year, currentBalance, currentDebt);
        MonthlyHistory.Add(snapshot);
        if (MonthlyHistory.Count > 24)
            MonthlyHistory.RemoveAt(0);

        CurrentMonth.Reset();
        return snapshot;
    }

    internal static void CopyStatementFields(FinancialStatement source, FinancialStatement target)
    {
        target.Month = source.Month;
        target.Year = source.Year;
        target.FreightRevenue = source.FreightRevenue;
        target.TicketRevenue = source.TicketRevenue;
        target.ContractBonusRevenue = source.ContractBonusRevenue;
        target.MunicipalSubsidyRevenue = source.MunicipalSubsidyRevenue;
        target.TollRevenue = source.TollRevenue;
        target.FleetMaintenanceExpense = source.FleetMaintenanceExpense;
        target.RoadConstructionExpense = source.RoadConstructionExpense;
        target.RoadMaintenanceExpense = source.RoadMaintenanceExpense;
        target.LoanInterestExpense = source.LoanInterestExpense;
        target.InfrastructureAndFleetExpense = source.InfrastructureAndFleetExpense;
        target.ClosingBalance = source.ClosingBalance;
        target.ClosingDebt = source.ClosingDebt;
    }

    internal void RestoreState(FinancialStatement currentMonth, FinancialStatement lifetime, IEnumerable<FinancialStatement> history)
    {
        CopyStatementFields(currentMonth, CurrentMonth);
        CopyStatementFields(lifetime, Lifetime);
        MonthlyHistory.Clear();
        MonthlyHistory.AddRange(history);
    }
}

