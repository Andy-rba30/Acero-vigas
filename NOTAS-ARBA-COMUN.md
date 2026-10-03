# Notas para ARBA-comun (desde Acero-vigas)

Cosas vistas al integrar `v1.0.0` que tocan al código común o a su guía; aquí solo se anotan (no se cambia nada en
`external/ARBA-comun` desde este repo).

1. **`Arba.Comun.props` duplica los `.cs` cuando el submódulo está dentro del proyecto.** Con el submódulo en
   `external/ARBA-comun` (la ruta que recomienda `INTEGRACION.md`), el glob por defecto del SDK (`**/*.cs`) ya
   compila `external/ARBA-comun/**/*.cs` y el `.props` los vuelve a añadir: 13 avisos `CS2002: Source file
   specified multiple times`. Además el glob arrastra `tests/Program.cs` y `build/CheckUsage.cs` del común al
   ensamblado del add-in. Solución aplicada en el add-in (`BeamRebar.csproj`):

   ```xml
   <DefaultItemExcludes>$(DefaultItemExcludes);external/**</DefaultItemExcludes>
   ```

   Propuesta para 1.0.1: que el `.props` lo añada él mismo (`$(ArbaComunDir)**` relativo al proyecto) o que
   `INTEGRACION.md` §2 lo diga junto al `Import`.
