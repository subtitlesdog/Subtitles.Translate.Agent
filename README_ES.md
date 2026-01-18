<div align="center">

[English](README.md) | [简体中文](README_CN.md) | [日本語](README_JP.md) | [Español](README_ES.md) | [Français](README_FR.md) | [Deutsch](README_DE.md)

</div>

---

### 📖 Introducción al Proyecto
**Motor de Subtítulos Inteligente Disruptivo**. Basado en la tecnología de **Colaboración Multi-Agente**, automatiza todo el proceso, entregando trabajos de traducción **casi a nivel humano**.

### ✨ Destacados
- **Estilo Unificado**: Genera primero una guía de estilo para toda la película (tono/estilo/estrategia de tratamiento), evitando la sensación de "dos traductores diferentes".
- **Terminología Consistente**: Genera y aplica automáticamente un glosario, unificando nombres/lugares/nombres propios y género de pronombres (él/ella).
- **Desambiguación de Contexto**: Utiliza una ventana deslizante para referenciar traducciones anteriores + vistas previas futuras, reduciendo referencias ambiguas y errores de segmentación de oraciones.
- **Auditoría Semántica**: Bucle de revisión incorporado específicamente para verificar errores de traducción/omisiones/alucinaciones; también se adhiere a protocolos de encabalgamiento, no completando oraciones a medias aleatoriamente.
- **Más Humano**: Encabalgamiento + traducción deslizante multilínea, haciendo el tono más coherente y reduciendo el "sabor a traducción automática".
- **Subtítulos Multi-Formato**: Soporta múltiples formatos de entrada de subtítulos comunes (detección automática).
- **Ahorro de Tokens**: Formato de subtítulos compacto, reduciendo el consumo de Tokens y mejorando la eficiencia del procesamiento.


### Inicio Rápido

#### Requisitos del Entorno
- .NET SDK 10.0 (Este proyecto TargetFramework es net10.0)
- API Key y Endpoint de LLM disponibles (personalizable en la entrada de inicio)

#### Ejecutar
Ejecute en el directorio raíz del proyecto:

```powershell
cd src
dotnet restore
dotnet run --project .\Subtitles.Translate.Agent\Subtitles.Translate.Agent.csproj
```

#### Uso Interactivo
Después del inicio, el programa solicitará secuencialmente:
- Ingrese la ruta local del archivo de subtítulos (soporta arrastrar y soltar a la terminal)
- Ingrese el idioma de destino (Enter por defecto a: Chino Simplificado)
- Ingrese la API Key (solicita si no está configurada)

#### Archivos de Salida
- Después de la traducción, se generará en el mismo directorio que el subtítulo original: `NombreArchivoOriginal.<idiomaDestino>.srt`
- Por defecto escribe traducción monolingüe (`GenerateTranslatedSrt()`); para subtítulos bilingües, cambie el punto de entrada a `GenerateBilingualSrt()`

#### Modelos y Endpoints Personalizados
La configuración de entrada se encuentra en la inicialización de `AgentSystemConfig` en [Program.cs:L90-L105](src/Subtitles.Translate.Agent/Program.cs#L90-L105), donde se pueden modificar `ModelId`, `Endpoint`, `ApiKey`, etc.

```csharp
// src/Subtitles.Translate.Agent/Program.cs
var systemConfig = new AgentSystemConfig();
systemConfig.AddDefaultConfig(new AgentConfig
{
    ModelId = "gpt-oss-120b",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
```

Ejemplo de modificación (usando modelos de contexto largo para Agente 1/2, modelo de menor costo para traducción):

```csharp
var systemConfig = new AgentSystemConfig();
systemConfig.AddConfig(nameof(Step1_DirectorAgent), new AgentConfig
{
    ModelId = "gemini-3-flash",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
systemConfig.AddConfig(nameof(Step2_GlossaryAgent), new AgentConfig
{
    ModelId = "gemini-3-flash",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
systemConfig.AddConfig(nameof(Step3_TranslatorAgent), new AgentConfig
{
    ModelId = "gpt-oss-120b",
    ApiKey = “apiKey”,
    Endpoint = "https://<your-endpoint>/v1"
});
```

#### Modelos Recomendados (Compensación Costo/Contexto)
- Agente 1 (Director) / Agente 2 (Glosario): Recomendar `gemini-3-flash` (contexto más largo, menor costo, mejor para escaneo global y extracción de terminología)
- Etapa de Traducción (Traductor): Puede usar modelos de menor costo, por ejemplo, `gpt-oss-120b`



### 1. Step1_DirectorAgent (Comprensión Global / Guía de Estilo)
- **Qué hace**: Lee primero el subtítulo completo, generando una "guía de película completa" para que las traducciones posteriores la sigan estrictamente, resolviendo la deriva de estilo y la confusión de tratamiento.
- **Entrada**: Subtítulos completos (formateados compactos), idioma de destino y otros parámetros de solicitud.

### 2. Step2_GlossaryAgent (Extracción de Terminología / Restricción de Consistencia)
- **Qué hace**: Basado en la estrategia del Paso 1 y subtítulos completos, extrae entidades clave y construye un glosario controlado, asegurando "un sustantivo, una traducción".
- **Entrada**: Guía de Estilo del Paso 1 + Subtítulos Completos.
- **Salida**: Tabla de personajes (incluyendo alias e inferencia de género), tabla de ubicaciones, tabla de terminología (incluyendo dominio y definición), y proporciona una versión Markdown directamente incrustable en prompts.

### 3. Step3_TranslatorAgent (Traducción de Ventana Deslizante / Validación Fuerte de Formato)
- **Qué hace**: Traduce subtítulos en lotes usando una ventana deslizante, referenciando el contexto anterior y vistas previas futuras para reducir errores de segmentación, referencia y ambigüedad.
- **Entrada**: Guía de Estilo del Paso 1 + Glosario del Paso 2 + Contexto Traducido Anterior + Lote Actual + Vista Previa Futura.
- **Salida**: Traducción inicial línea por línea (forzada a mantener IDs originales y cantidad consistente); opcionalmente activa el Paso 4 para auditoría semántica antes de escribir el borrador final.

### 4. Step4_ReviewerAgent [Por Ser Código Abierto] (Auditoría Semántica / Protocolo de Retrotraducción)
- **Qué hace**: Solo realiza correcciones de "nivel de auditoría" para precisión semántica, verificando específicamente errores de traducción, omisiones y alucinaciones; sin pulido, sin embellecimiento de terminología.
- **Entrada**: Lote de Traducción Inicial del Paso 3.
- **Salida**: PASS/FIXED línea por línea, motivo del error (crítica) y traducción final adoptada (final_translation), estrictamente alineada con la cantidad del Paso 3.

### 5. Step5_PolisherAgent [Por Ser Código Abierto] (Cumplimiento de Terminología + Pulido de Flujo)
- **Qué hace**: Sin romper los puntos de corte de la línea de tiempo, primero impone correcciones de terminología y pronombres, luego realiza un pulido de expresión más nativo y optimización del ritmo.
- **Entrada**: Glosario del Paso 2 + Guía de Estilo del Paso 1 + Traducción de Lote Actual + Resultado Pulido Anterior (para transición coherente).
- **Salida**: polished_text, nota opcional (explicación de jerga/terminología), optimization_tag (corrección de terminología/pulido de contexto/adaptación de estilo/sin cambio).

### 6. Step6_TimingAdjusterAgent [Por Ser Código Abierto] (Ajuste Fino de Línea de Tiempo para Comodidad de Lectura)
- **Qué hace**: Extiende automáticamente end_time basado en la longitud de la traducción y el tiempo de inicio de la siguiente oración para mejorar la legibilidad; solo cambia el tiempo de finalización, no toca el tiempo de inicio, no se permite superposición.
- **Entrada**: Texto traducido, inicio/fin original, inicio de la siguiente oración (incluyendo búfer de seguridad de 50ms).
- **Salida**: KEEP/EXTEND, adjusted_end, razón, y aplica ajustes al objeto de subtítulo.

## 📅 Plan de Código Abierto
- **Febrero 2026**: Abrir Step6_TimingAdjusterAgent
- **Marzo 2026**: Desarrollar UI de Windows / macOS / Web

## 🙏 Agradecimientos

Este proyecto utiliza los siguientes excelentes proyectos de código abierto:

- **[Subtitle Edit](https://github.com/SubtitleEdit/subtitleedit)** (libse): Potente biblioteca central de edición y procesamiento de subtítulos.
- **[Microsoft Agents](https://github.com/microsoft/agents)**: Marco base para construir Agentes inteligentes.
- **[Mscc.GenerativeAI](https://github.com/mscirts/Mscc.GenerativeAI)**: Proporciona soporte .NET para modelos Google Gemini.
