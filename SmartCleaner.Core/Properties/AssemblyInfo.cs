using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SmartCleaner.Core.Tests")]

// День 23 — перенос Дня 19 (M7): CliInspectorViewModel.OpenTerminal резолвит
// powershell.exe через internal SystemToolLocator — единственное санкционированное
// место комбинации имени утилиты с системным каталогом.
[assembly: InternalsVisibleTo("SmartCleaner.App")]
