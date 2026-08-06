using AI.Factory.Core.Domain;
using AI.Factory.Core.Machines;

namespace AI.Factory.UnitTests;

public sealed class MachineRuleTests
{
    [Theory]
    [InlineData(72, RiskStatus.Normal)]
    [InlineData(84.99, RiskStatus.Normal)]
    [InlineData(85, RiskStatus.Warning)]
    [InlineData(90, RiskStatus.Warning)]
    [InlineData(94.99, RiskStatus.Warning)]
    [InlineData(95, RiskStatus.Critical)]
    public void Running_machine_alert_status_matches_the_locked_boundaries(decimal temperature, RiskStatus expected) =>
        Assert.Equal(expected, MachineRules.CalculateAlertStatus(MachineRunningStatus.Running, temperature));

    [Theory]
    [InlineData(35)]
    [InlineData(0)]
    [InlineData(999)]
    public void A_stopped_machine_is_always_a_warning_regardless_of_temperature(decimal temperature) =>
        Assert.Equal(RiskStatus.Warning, MachineRules.CalculateAlertStatus(MachineRunningStatus.Stopped, temperature));
}
