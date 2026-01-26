### GitHub Issue

**Title:** Fix build failure: Resource file **/*.resx cannot be found in Signage.Server

**Body:**

## Problem
The build is failing in the CI/CD pipeline with the following error:

```
error MSB3552: Resource file "**/*.resx" cannot be found. [/src/src/Signage.Server/Signage.Server.csproj]
```

**Failed Job:** https://github.com/glensouza/PlainSight/actions/runs/21348263433/job/61439804666  
**Commit:** b83b7b98abdde852b36d395953e90ed236f4f6f4

## Root Cause
MSBuild is attempting to find .resx resource files that do not exist in the project. This can happen when:
- The project or referenced projects have wildcards referencing .resx files that don't exist
- Default resource globbing is enabled but no resource files are present

## Solution
Add the following property to `src/Signage.Server/Signage.Server.csproj` to disable default resource file globbing if no resource files are being used:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
  <EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>
</PropertyGroup>
```

Alternatively, if resource files are needed, ensure all expected .resx files are present in the correct locations and committed to the repository.

## Files to Check
- `src/Signage.Server/Signage.Server.csproj`
- `src/Signage.Shared/Signage.Shared.csproj` (referenced project)
- `src/PlainSight.ServiceDefaults/PlainSight.ServiceDefaults.csproj` (referenced project)