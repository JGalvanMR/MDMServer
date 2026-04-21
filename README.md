# 📦 **Repositorio: MDMServer**

## 🧠 Descripción General

Backend central del sistema MDM (Mobile Device Management) desarrollado en **ASP.NET Core**.
Su responsabilidad es gestionar dispositivos, usuarios, comandos remotos, monitoreo y comunicación en tiempo real mediante WebSockets.

---

## 🎯 Propósito

Centralizar el control de dispositivos Android permitiendo:

* Registro y autenticación de dispositivos
* Envío de comandos remotos
* Recepción de telemetría (estado, ubicación, etc.)
* Streaming de pantalla en tiempo real
* Administración desde panel web

---

## 🏗️ Arquitectura

Arquitectura en capas:

```
Controllers → Services → Repositories → Database
                ↓
             DTOs / Validators
                ↓
           Middleware / Hubs
```

### Componentes clave

| Capa            | Responsabilidad                  |
| --------------- | -------------------------------- |
| Controllers     | Exponen endpoints REST           |
| Services        | Lógica de negocio                |
| Repositories    | Acceso a datos (SQL Server)      |
| DTOs            | Contratos de entrada/salida      |
| Validators      | Validación con FluentValidation  |
| Middleware      | Manejo global (errores, logging) |
| Hubs/WebSockets | Comunicación en tiempo real      |

---

## 📁 Estructura

```
/Controllers
/Services
/Repositories
/DTOs
/Validators
/Middleware
/Hubs
/Data
/Models
```

---

## 🔌 Endpoints principales

### REST

* `/api/devices`
* `/api/commands`
* `/api/auth`
* `/api/monitoring`

### WebSockets

* `/ws/device` → comunicación con dispositivos
* `/ws/viewer` → clientes web para monitoreo/streaming

---

## 🔄 Flujos clave

### 1. Registro de dispositivo

1. Cliente Android envía request con `ADMIN_KEY`
2. Backend valida
3. Se registra dispositivo en BD
4. Se retorna token o confirmación

---

### 2. Ejecución de comandos

1. Admin envía comando desde panel
2. Backend lo guarda
3. Se envía vía WebSocket al dispositivo
4. Dispositivo ejecuta
5. Retorna resultado

---

### 3. Streaming de pantalla

1. Panel abre WebSocket `/ws/viewer`
2. Solicita `watch(deviceId)`
3. Backend conecta con `/ws/device`
4. Cliente transmite H.264
5. Panel renderiza (Broadway.js)

---

## 🗄️ Base de datos

### Entidades principales

| Tabla          | Descripción              |
| -------------- | ------------------------ |
| Devices        | Dispositivos registrados |
| Commands       | Comandos enviados        |
| CommandResults | Resultado de ejecución   |
| DeviceStatus   | Estado/heartbeat         |
| Users          | Usuarios admin           |

---

## ⚙️ Configuración

### Variables clave

* `ConnectionStrings:Default`
* `ADMIN_KEY`
* Configuración de CORS
* Logging (Serilog)

---

## ▶️ Ejecución

```bash
dotnet restore
dotnet run
```

Swagger disponible en:

```
/swagger
```

---

## ⚠️ Observaciones técnicas

* Ya implementa separación por capas (buena base)
* WebSockets son críticos para streaming y comandos
* Falta posible:

  * CQRS (opcional)
  * Mejor manejo de reconexiones WS
  * Escalabilidad horizontal (Redis/pub-sub)

---

---
