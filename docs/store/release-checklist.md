# Store release checklist

- [ ] Register the Partner Center developer account.
- [ ] Reserve the final English and Simplified Chinese product names.
- [ ] Associate the WinUI project with the Store product, replacing the placeholder identity and publisher.
- [ ] Set the final support email, support URL, and hosted privacy-policy URL.
- [ ] Review and publish both localized Store listings.
- [ ] Capture at least one current screenshot for each Store listing language.
- [ ] Install Visual Studio Windows application development and MSIX components.
- [ ] Run `dotnet test tests\PowerManager.Core.Tests\PowerManager.Core.Tests.csproj -c Release`.
- [ ] Run `scripts\Build-StorePackages.ps1 -StoreUpload`.
- [ ] Install and test x86, x64, and ARM64 packages on representative devices or VMs.
- [ ] Run the Windows App Certification Kit on the signed release package.
- [ ] Confirm `runFullTrust` is the only restricted capability and paste the justification from `certification-notes.md`.
- [ ] Confirm the package contains no development certificate, private key, Store association secret, or test settings.
- [ ] Upload the generated `.msixupload` files and include the certification test flow.
