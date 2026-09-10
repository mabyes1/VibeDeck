using VibeDeck.Host;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class AppPathsTests
    {
        [Fact]
        public void CommonApplicationData_keeps_a_real_absolute_path()
        {
            var result = AppPaths.ResolveCommonApplicationData(
                @"D:\SharedData",
                @"C:\Windows");

            Assert.Equal(@"D:\SharedData", result);
        }

        [Fact]
        public void CommonApplicationData_recovers_from_unexpanded_SystemDrive_literal()
        {
            var result = AppPaths.ResolveCommonApplicationData(
                @"%SystemDrive%\ProgramData",
                @"C:\Windows");

            Assert.Equal(@"C:\ProgramData", result);
        }

        [Fact]
        public void CommonApplicationData_falls_back_to_C_drive_without_windows_environment()
        {
            var result = AppPaths.ResolveCommonApplicationData("", "");

            Assert.Equal(@"C:\ProgramData", result);
        }
    }
}
