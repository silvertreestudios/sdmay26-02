# Unity Tests Setup

This document describes the Unity test setup for this project.

## Overview

The project now includes:
- Unity Test Framework package (v1.3.11)
- Sample EditMode tests in `Assets/Tests/`
- GitHub Actions workflow for automated testing

## Running Tests Locally

### In Unity Editor:
1. Open the project in Unity Editor
2. Go to Window → General → Test Runner
3. Click on the EditMode tab
4. Click "Run All" to execute all tests

### From Command Line:
```bash
# Unity installation path may vary based on your OS
/path/to/Unity -runTests -batchmode -projectPath . -testResults results.xml -testPlatform EditMode
```

## Test Structure

### Assets/Tests/
Contains all test files:
- `Tests.asmdef` - Assembly definition for test code
- `SampleTests.cs` - Basic C# unit tests (addition, strings, booleans)
- `UnityObjectTests.cs` - Unity-specific tests (GameObject, Vector3, Color)

## GitHub Actions Workflow

The `.github/workflows/unity-tests.yml` workflow automatically runs tests on:
- Push to `main` or `develop` branches
- Pull requests targeting `main` or `develop` branches
- Manual trigger via GitHub Actions UI

### Required Secrets

To enable the GitHub Actions workflow, configure these repository secrets:
1. `UNITY_LICENSE` - Unity license file content (can be acquired by activating Unity locally)
2. `UNITY_EMAIL` - Unity account email
3. `UNITY_PASSWORD` - Unity account password

### Acquiring Unity License for CI

You can get a Unity license file for CI/CD:
1. Activate Unity locally with your credentials
2. Use the Unity License activation tool or manual activation
3. For free Unity licenses (Personal/Student), see [Unity CI/CD documentation](https://game.ci/docs/github/activation)

## Adding New Tests

To add new tests:
1. Create a new C# file in `Assets/Tests/`
2. Use the `[Test]` attribute from NUnit framework
3. Follow the Arrange-Act-Assert pattern
4. Tests will automatically be discovered by the Test Runner

Example:
```csharp
using NUnit.Framework;

namespace Tests
{
    public class MyNewTests
    {
        [Test]
        public void MyTestMethod()
        {
            // Arrange
            int expected = 42;
            
            // Act
            int actual = 40 + 2;
            
            // Assert
            Assert.AreEqual(expected, actual);
        }
    }
}
```

## Notes

- Current tests are fake/sample tests for demonstration purposes
- The test framework is ready for real unit tests to be added
- Tests run in EditMode (no Play Mode tests configured yet)
- No existing code was modified during this setup
