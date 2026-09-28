# A Simple Minecraft Server v1.5.9 Build 2

## Compiler fix
- Added the missing `FileOperationService.CopyDirectory` implementation used by world duplication.
- Added recursive file and directory copying with path containment checks.
- Rejects missing source folders and existing destination folders.
- Removes a partially copied destination when copying fails.

## Validation
- ZIP integrity checked.
- XAML files parsed successfully.
- Final Windows compilation must be verified with `Build-And-Run.bat`.
