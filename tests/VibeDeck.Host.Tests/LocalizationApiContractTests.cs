using VibeDeck.Host.Display;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class LocalizationApiContractTests
    {
        [Fact]
        public void Login_result_keeps_a_stable_code_alongside_the_legacy_message()
        {
            var failure = HostLoginResult.Fail("密碼不正確。", "auth.invalid_password");
            var success = HostLoginResult.Ok("session-token", System.TimeSpan.FromMinutes(5));

            Assert.False(failure.Success);
            Assert.Equal("auth.invalid_password", failure.Code);
            Assert.Equal("密碼不正確。", failure.Message);
            Assert.True(success.Success);
            Assert.Equal("auth.success", success.Code);
        }

        [Theory]
        [InlineData("ready", "display.ready")]
        [InlineData("installing", "display.installing")]
        [InlineData("installed", "display.installed")]
        [InlineData("repair-ready", "display.repair_ready")]
        [InlineData("console-required", "display.console_required")]
        public void Virtual_display_status_exposes_a_localizable_code(string state, string code)
        {
            VirtualDisplayInstallStatus status = state switch
            {
                "ready" => VirtualDisplayInstallStatus.Ready(),
                "installing" => VirtualDisplayInstallStatus.Installing(),
                "installed" => VirtualDisplayInstallStatus.Installed(),
                "repair-ready" => VirtualDisplayInstallStatus.RepairReady(),
                _ => VirtualDisplayInstallStatus.ConsoleRequired()
            };

            Assert.Equal(state, status.State);
            Assert.Equal(code, status.Code);
            Assert.False(string.IsNullOrWhiteSpace(status.Message));
        }
    }
}
