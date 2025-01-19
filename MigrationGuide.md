# Migration Guide

## Overview
The `DependencyInjection.SourceGenerator.Microsoft.Contracts` package has been deprecated. The attributes and enums previously provided by this package are now generated as internal attributes in each project. This change eliminates the possibility of version conflicts.

## Changes

### Namespace Changes
- Old Namespace: `DependencyInjection.SourceGenerator.Microsoft.Contracts.Attributes`
- New Namespace: `Microsoft.Extensions.DependencyInjection`

### Enum Changes
- Old Enum: `DependencyInjection.SourceGenerator.Microsoft.Contracts.Enums.Lifetime`
- New Enum: `Microsoft.Extensions.DependencyInjection.ServiceLifetime`

## Migration Steps

1. **Remove the old package:**
   ```sh
   dotnet remove package DependencyInjection.SourceGenerator.Microsoft.Contracts
   ```

2. **Update your code to use the new namespaces and enums:**

   - Replace:
     ```csharp
     using DependencyInjection.SourceGenerator.Microsoft.Contracts.Attributes;
     using DependencyInjection.SourceGenerator.Microsoft.Contracts.Enums;
     ```

   - With:
     ```csharp
     using Microsoft.Extensions.DependencyInjection;
     ```

3. **Update attribute usage:**
   - Old:
     ```csharp
     [Register(Lifetime = Lifetime.Singleton)]
     ```
   - New:
     ```csharp
     [Register(Lifetime = ServiceLifetime.Singleton)]
     ```

4. **Rebuild your project:**
   ```sh
   dotnet build
   ```

Following these steps will help you migrate to the new version of the library without any issues.
