# MDM Server API

Backend para administración remota de dispositivos Android (MDM), construido con ASP.NET Core.  
El sistema expone endpoints HTTP y un canal WebSocket para autenticación de dispositivos, recepción de estado (heartbeat) y reporte de resultados de comandos remotos.

## Características principales

- API HTTP basada en controladores
- Validación de entrada con FluentValidation
- Logging estructurado con Serilog
- Documentación OpenAPI/Swagger
- Health checks
- Comunicación bidireccional con dispositivos mediante WebSocket
- Arquitectura por capas con servicios y repositorios
- Integración con SQL Server

## Tecnologías

- .NET / ASP.NET Core
- FluentValidation
- Serilog
- Swagger / OpenAPI
- SQL Server
- WebSockets
- System.Text.Json

## Arquitectura

El sistema sigue una arquitectura por capas:

- **API / Presentación**: Controladores ASP.NET Core y endpoint WebSocket
- **Servicios**: Lógica de negocio para dispositivos, comandos y geocercas
- **Repositorios**: Persistencia en base de datos
- **Infraestructura**: Logging, middleware, validación, health checks y servicios hospedados

## Componentes principales

### Program.cs
Punto de entrada de la aplicación. Configura:

- Serilog
- Controladores y validación
- Swagger
- CORS
- Health checks
- Inyección de dependencias
- Middleware global
- WebSockets
- Verificación de conexión a SQL Server al arranque

## Seguridad

Swagger documenta dos mecanismos de autenticación por header:

- `Device-Token`: token del dispositivo
- `X-Admin-Key`: clave administrativa

Además, el sistema genera un Request ID si el cliente no lo envía.

## Endpoint WebSocket

### `GET /ws/device`

Canal WebSocket para dispositivos autenticados.

#### Requisitos
- La solicitud debe ser WebSocket
- Debe incluir el header `Device-Token`

#### Mensajes soportados

##### `RESULT`
Reporte del resultado de un comando ejecutado por el dispositivo.

Ejemplo:
```json
{
  "type": "RESULT",
  "commandId": 123,
  "success": true,
  "resultJson": "{\"status\":\"ok\"}",
  "errorMessage": null
}
