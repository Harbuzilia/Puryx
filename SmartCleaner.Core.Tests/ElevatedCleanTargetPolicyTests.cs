using SmartCleaner.Core.Cleaning;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class ElevatedCleanTargetPolicyTests
{
    [Fact]
    public void ValidateContract_ReturnsTrueForDefaultContract()
    {
        // Arrange
        var contract = ElevatedCleanTargetPolicy.CreateContract();

        // Act
        var isValid = ElevatedCleanTargetPolicy.ValidateContract(contract);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateContract_ReturnsFalseForWrongContractId()
    {
        // Arrange
        var contract = ElevatedCleanTargetPolicy.CreateContract() with
        {
            ContractId = "smartcleaner.elevated-clean.targets.v0"
        };

        // Act
        var isValid = ElevatedCleanTargetPolicy.ValidateContract(contract);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateContract_ReturnsFalseWhenAllowedRootsSetIsModified()
    {
        // Arrange
        var contract = ElevatedCleanTargetPolicy.CreateContract();
        contract.AllowedRoots.RemoveAt(0);

        // Act
        var isValid = ElevatedCleanTargetPolicy.ValidateContract(contract);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void IsAllowedTarget_ReturnsTrueForPathInsideAllowedRoot()
    {
        // Arrange
        var allowedRoot = ElevatedCleanTargetPolicy.CreateContract().AllowedRoots.First();
        var allowedPath = Path.Combine(allowedRoot, "smartcleaner-policy-test", "file.tmp");

        // Act
        var allowed = ElevatedCleanTargetPolicy.IsAllowedTarget(allowedPath);

        // Assert
        Assert.True(allowed);
    }

    [Fact]
    public void IsAllowedTarget_ReturnsFalseForArbitraryPath()
    {
        // Arrange
        var randomPath = Path.Combine(Path.GetTempPath(), "smartcleaner-arbitrary");

        // Act
        var allowed = ElevatedCleanTargetPolicy.IsAllowedTarget(randomPath);

        // Assert
        Assert.False(allowed);
    }
}
