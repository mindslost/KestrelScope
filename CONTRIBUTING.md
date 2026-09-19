# Contributing to KestrelScope

Thank you for your interest in contributing to KestrelScope! We welcome community contributions, bug reports, feature suggestions, and pull requests from individuals and organizations alike.

---

## License & Intellectual Property Notice

Before contributing, please review the project's licensing model:

1. **Source-Available Licensing**: KestrelScope is distributed under the **Apache License, Version 2.0** with the **"Commons Clause" License Condition v1.0** (see [LICENSE.md](LICENSE.md)).
2. **Bound Submissions**: In accordance with Section 5 of the Apache License 2.0, any Contribution intentionally submitted for inclusion in KestrelScope by you to the Licensor shall be subject to the terms and conditions of the root license (Apache License 2.0 with Commons Clause Condition v1.0), without any additional terms or conditions.
3. **Usage Terms**: The software remains free for individuals and companies to use, inspect, adapt, and build upon for internal operations. However, bundling, reselling, or offering KestrelScope as a commercial hosted service without explicit authorization from the Licensor is strictly prohibited under the Commons Clause.

---

## How to Contribute

### 1. Reporting Bugs
If you encounter unexpected behavior or defects:
- Check existing issues in the GitHub repository to avoid duplicates.
- Open a new issue providing:
  - Clear description of the problem
  - Steps to reproduce
  - Expected vs. actual behavior
  - Environment details (.NET SDK version, OS, browser, Docker environment)
  - Relevant logs or stack traces (ensure sensitive credentials/tokens are redacted)

### 2. Suggesting Enhancements
Feature requests are welcome:
- Open a GitHub issue or discussion outlining the proposed feature, the problem it solves, and potential design approaches.
- For significant architectural or UI changes, discussing the concept first helps ensure alignment with KestrelScope's sovereign, single-binary principles.

### 3. Submitting Pull Requests

Follow these steps when contributing code:

1. **Fork and Branch**:
   - Fork the repository and create a branch from `main`:
     ```bash
     git checkout -b feature/your-feature-name
     # or
     git checkout -b fix/your-bugfix-name
     ```

2. **Code Standards & Architecture Mandates**:
   - **Single-Binary & Air-Gapped**: Maintain zero external database requirements and zero external CDN/asset dependencies.
   - **Backend**: Adhere to modern C# / .NET 9 best practices, nullable reference types, and async I/O patterns.
   - **Frontend**: Follow the Microsoft Fluent 2 Dark Design tokens in `wwwroot/css/main.css` and use local embedded SVGs.

3. **Verify & Run Tests**:
   - Verify that the solution compiles cleanly without errors:
     ```bash
     dotnet build KestrelScope.sln -c Release
     ```
   - Run the end-to-end automated test suite:
     ```bash
     dotnet test tests/KestrelScope.EndToEndTests/KestrelScope.EndToEndTests.csproj
     ```

4. **Submit Your PR**:
   - Push your branch to your fork and submit a Pull Request targeting `main`.
   - Provide a concise summary of changes, motivation, and any verification steps completed.
   - Ensure all automated CI checks pass.

Thank you for helping make KestrelScope better for everyone!

