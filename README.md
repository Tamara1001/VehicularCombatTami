# 🚀 Vehicle Combat — Motores Gráficos II

> **Proyecto Final de Carrera · Producción de Simuladores y Videojuegos**  
> Universidad · Motores Gráficos II — 2026

---

<p align="center">
  <img src="https://img.shields.io/badge/Engine-Unity%206-black?logo=unity&style=for-the-badge"/>
  <img src="https://img.shields.io/badge/Lenguaje-C%23-239120?logo=c-sharp&style=for-the-badge"/>
  <img src="https://img.shields.io/badge/Género-Vehicular%20Combat-blueviolet?style=for-the-badge"/>
  <img src="https://img.shields.io/badge/Entorno-Lunar%20%2F%20Espacial-1a1a2e?style=for-the-badge"/>
</p>

---

## 📖 Descripción General

**Vehicle Combat** es un juego de combate vehicular de vista cenital (*Top-Down*) ambientado en un entorno lunar y espacial. El jugador controla un vehículo armado y debe enfrentarse a oleadas de enemigos con inteligencia artificial autónoma, en un campo de batalla con física personalizada que simula la baja gravedad característica de la Luna.

El proyecto fue desarrollado como entrega final para la materia **Motores Gráficos II**, con énfasis en la aplicación práctica de patrones de arquitectura de software, optimización de rendimiento en tiempo real, e implementación de sistemas de renderizado avanzados dentro del motor **Unity**.

---

## ✨ Características Técnicas Principales

Esta sección detalla las decisiones de diseño e implementación más relevantes del proyecto desde una perspectiva técnica y académica.

---

### 🤖 1. Arquitectura de Inteligencia Artificial — Máquina de Estados Finitos (FSM)

El comportamiento de todos los enemigos está gobernado por una **Máquina de Estados Finitos** implementada en la clase base `EnemyVehicleBase.cs`. Esta arquitectura garantiza que cada transición de estado sea auditable, predecible y extensible sin modificar el código existente (**Principio Abierto/Cerrado — OCP**).

**Estados definidos:**

| Estado | Descripción |
|---|---|
| `Patrol` | El enemigo navega aleatoriamente por el área de patrulla usando NavMesh. |
| `Chase` | Detectado el jugador dentro del radio de detección, el enemigo lo persigue activamente. |
| `Attack` | El enemigo ejecuta su comportamiento de ataque específico (sobrescrito por subclases). |
| `Stunned` | El vehículo pierde el control temporalmente tras recibir un impacto especial. |
| `Recover` | Maniobra de recuperación autónoma cuando el vehículo queda atascado en geometría. |

**Patrones aplicados:**
- **Template Method Pattern:** Los hooks virtuales (`OnPatrolUpdate`, `OnChaseUpdate`, `OnAttackUpdate`) permiten a las subclases inyectar comportamiento sin alterar la lógica de transición base.
- **Observer Pattern:** Todas las transiciones emiten el evento `OnStateChanged`, permitiendo que sistemas externos (VFX, audio, UI) reaccionen sin acoplamientos directos.
- **Sistema de Locomoción Híbrido:** El `NavMeshAgent` actúa exclusivamente como calculador de rutas. La locomoción física real es manejada por un `Rigidbody` con `AddForce`, desacoplando el pathfinding del movimiento físico.

---

### 🏎️ 2. Física Personalizada — Controlador de Vehículo Arcade

El movimiento del jugador está implementado mediante un controlador arcade propio (`ArcadeVehicleController.cs`) que no depende del sistema de vehículos estándar de Unity. Sus características clave incluyen:

- **Gravedad lunar personalizada:** Se desactiva la gravedad estándar de Unity (`useGravity = false`) y se aplica una fuerza gravitacional configurable mediante `Rigidbody.AddForce`, logrando que todos los objetos dinámicos floten con la inercia característica de un entorno de baja gravedad.
- **Fricción lateral:** Un método `ApplyLateralFriction()` cancela la velocidad lateral del vehículo en cada `FixedUpdate`, previniendo el deslizamiento lateral no deseado y otorgando una sensación de conducción responsiva.
- **Separación Update/FixedUpdate:** Toda la lógica de entrada y estado se procesa en `Update()`, mientras que el movimiento físico se aplica exclusivamente en `FixedUpdate()`, respetando el pipeline de física determinístico de Unity.

---

### ♻️ 3. Optimización de Rendimiento — Object Pooling de Proyectiles

Para evitar el costo de rendimiento de instanciar y destruir objetos frecuentemente (garbage collection), el sistema de proyectiles implementa el patrón **Object Pool** en `ProjectilePool.cs`.

**Funcionamiento:**

```
Disparo → Solicitar proyectil al Pool → Configurar y activar
Impacto/Expiración → Desactivar y devolver al Pool (nunca Destroy)
```

- Los proyectiles son **pre-instanciados** al inicio de la escena.
- Al ser "destruidos", simplemente se desactivan (`SetActive(false)`) y se devuelven a la cola disponible.
- La interfaz de activación (`IPoolable`) permite que el sistema de pool sea agnóstico al tipo de proyectil, facilitando la extensión futura con distintos tipos de munición.

---

### 💉 4. Sistema de Daño — Arquitectura Orientada a Eventos

El sistema de salud y daño está diseñado bajo el principio de **mínimo acoplamiento**. Ningún sistema del juego habla directamente con otro; en su lugar, todos se comunican a través de eventos y contratos de interfaz.

**Componentes:**

- **`IDamageable` (Interface):** Contrato que cualquier entidad que pueda recibir daño debe implementar. Los proyectiles llaman a `TakeDamage(int)` sobre esta interfaz, sin importar si el objetivo es el jugador, un enemigo, o cualquier objeto destructible futuro.

- **`HealthComponent.cs` (MonoBehaviour):** Implementa `IDamageable` y gestiona el estado de salud de forma encapsulada. Expone dos eventos de C#:
  - `OnHealthChanged`: Notifica a la UI el porcentaje de salud actual en tiempo real.
  - `OnDied`: Disparado una única vez cuando la salud llega a cero. Todas las entidades (enemigos, jugador) suscriben su lógica de muerte a este evento.

- **Pipeline de muerte del enemigo:**

```
TakeDamage() → HealthComponent.OnDied → OnHealthDepleted()
             → StopPhysics() → OnEnemyDied?.Invoke() → Destroy(gameObject)
```

El `WinConditionManager` se suscribe a `OnEnemyDied` para contabilizar bajas sin conocer los detalles internos del enemigo.

---

### 🌌 5. Renderizado — Shader Graph Personalizado

El entorno espacial es generado proceduralmente mediante un **Shader Graph** personalizado que crea el cielo estrellado del fondo sin necesidad de texturas externas:

- **Ruido Voronoi:** Genera distribuciones irregulares de puntos que simulan la posición natural de las estrellas.
- **Parámetros de exposición y densidad:** Controlables desde el Inspector para ajuste artístico en tiempo de edición.
- **Renderizado en la pasada de fondo:** Implementado como un material *Skybox* con blend de colores nebulosa configurables, creando profundidad visual sin impacto en el rendimiento de la escena.

---

## 🎮 Controles del Jugador

| Acción | Control |
|---|---|
| **Mover hacia adelante** | `W` / `↑` |
| **Mover hacia atrás** | `S` / `↓` |
| **Girar a la izquierda** | `A` / `←` |
| **Girar a la derecha** | `D` / `→` |
| **Apuntar** | `Mouse` (la torreta sigue el cursor) |
| **Disparar** | `Clic Izquierdo` |
| **Freno de mano** | `Barra Espaciadora` |

---

## 👾 Tipos de Enemigos

### ☠️ Kamikaze

El enemigo Kamikaze no posee armas de proyectiles. Su única estrategia es **cargarse directamente contra el jugador** a máxima velocidad para causarle daño por colisión. Cuando entra en el estado `Attack`, aplica un multiplicador de aceleración (*throttle multiplier*) por encima del límite de velocidad normal para garantizar que el impacto sea devastador.

> ⚡ **Señal de telegrafía:** El cambio brusco de trayectoria desde el estado `Chase` hacia el jugador es la única advertencia antes del impacto.

---

### 🔫 Shooter

El enemigo Shooter adopta una táctica de **órbita y fuego**. En lugar de atacar de frente, calcula una posición lateral desplazada respecto al jugador y navega en círculos alrededor de él, disparando su arma cada vez que el ángulo de la torreta queda alineado con el objetivo dentro de un margen configurable. Utiliza verificación de línea de visión (*Raycast Line-of-Sight*) para no disparar a través de geometría opaca.

> 🔄 **Comportamiento de reposicionamiento:** Si el jugador se acerca demasiado, el Shooter retrocede automáticamente a su distancia de combate preferida antes de reanudar el fuego.

---

## 🛠️ Instrucciones de Instalación y Ejecución

### Opción A — Abrir en Unity Editor

1. Clonar o descargar el repositorio:
   ```bash
   git clone https://github.com/Tamara1001/VehicularCombatTami.git
   ```
2. Abrir **Unity Hub** y seleccionar **"Open Project"**.
3. Navegar a la carpeta raíz del repositorio y confirmar.
4. Asegurarse de tener instalada la versión correcta de Unity (ver badge en la parte superior del README).
5. Una vez cargado el proyecto, abrir la escena principal desde:
   ```
   Assets/_Project/Scenes/MainScene.unity
   ```
6. Presionar el botón **▶ Play** en el Editor para ejecutar el juego.

### Opción B — Ejecutar el Build compilado

> *(Si se incluye un build precompilado en los releases del repositorio)*

1. Descargar el archivo `.zip` desde la sección **Releases** del repositorio.
2. Descomprimir en cualquier directorio local.
3. Ejecutar el archivo `VehicleCombat.exe`.
4. No se requiere instalación adicional.

---

## 📋 Requisitos del Sistema

| Componente | Mínimo recomendado |
|---|---|
| **SO** | Windows 10 / 11 (64-bit) |
| **CPU** | Intel Core i5 o equivalente |
| **RAM** | 8 GB |
| **GPU** | Compatible con DirectX 11 / OpenGL 4.5 |
| **Unity** | Unity 6 (6000.x) con Universal Render Pipeline (URP) |

---

## 👩‍💻 Créditos

| Rol | Nombre |
|---|---|
| **Desarrollo y Diseño** | D'Angelo, Tamara |
| **Desarrollo y Diseño** | Warner, Alejo |

> Proyecto desarrollado en el marco de la carrera **Producción de Simuladores y Videojuegos** · Materia: *Motores Gráficos II* · 2026.

---

<p align="center">
  <sub>Hecho con ❤️ y demasiado café — Vehicle Combat © 2026</sub>
</p>
