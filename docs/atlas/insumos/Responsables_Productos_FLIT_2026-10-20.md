# Responsables por producto y por módulo

**Fecha:** 2026-10-20 · **Definió:** Jorman Copete (Líder técnico) · **Versión 5:** reparto definido por el líder técnico

---

## 1. Qué significa cada rol

**Responsable principal**
- Dueño técnico del producto o módulo: backlog técnico, calidad, decisiones de diseño, despliegues y atención de incidentes.
- Revisa (o delega la revisión de) todo PR que toque su módulo.
- Mantiene al día la documentación (README, manual de operación, ADRs del módulo).

**Responsable secundario**
- Respaldo real: debe poder desarrollar, desplegar y atender un incidente sin el principal.
- Revisa PRs del módulo como segundo aprobador y participa en las decisiones grandes.
- Dedicación orientativa: ~20 % de su tiempo en ese módulo.

---

## 2. Reparto por persona

| Persona | Principal de | Secundario de |
|---|---|---|
| **Jorman Copete** | Firmas, Sentinel, Atlas, flit-scheduler, Plataforma | Vigía, Administradores, Identidad, Infraestructura |
| **Juan Montoya** | Diagnóstico, ICT, Integraciones, Log, Consultas | Comparendos, Suite, Migración, Trámites, Notificaciones |
| **Daniel Mejía** | Vigía, Infraestructura | Sentinel, Certify, flit-scheduler |
| **David Chica** | FLITO, People | Usuarios, Reportes, Ayuda, Doctor FLIT |
| **Willyn Londoño** | Trámites, Administradores | Diagnóstico, ICT, Integraciones, Log, Consultas |
| **Samuel Cárdenas** | Comparendos, Suite, Migración, Identidad, Usuarios, Notificaciones | Atlas, FLITO, People, Plataforma |
| **Daniel Amado** | Certify, Reportes, Ayuda, Doctor FLIT | Firmas |

---

## 3. Reparto por producto y módulo

### 3.1 Productos

| Producto | Principal | Secundario |
|---|---|---|
| Vigía | Daniel Mejía | Jorman Copete |
| Certify | Daniel Amado | Daniel Mejía |
| FLITO | David Chica | Samuel Cárdenas |
| Firmas | Jorman Copete | Daniel Amado |
| Sentinel | Jorman Copete | Daniel Mejía |
| Atlas | Jorman Copete | Samuel Cárdenas |
| People | David Chica | Samuel Cárdenas |
| flit-scheduler | Jorman Copete | Daniel Mejía |
| Infraestructura | Daniel Mejía | Jorman Copete |

### 3.2 Módulos de FLIT 2

| Módulo | Principal | Secundario |
|---|---|---|
| Trámites | Willyn Londoño | Juan Montoya |
| Diagnóstico | Juan Montoya | Willyn Londoño |
| Comparendos | Samuel Cárdenas | Juan Montoya |
| Administradores | Willyn Londoño | Jorman Copete |
| Usuarios | Samuel Cárdenas | David Chica |
| Identidad | Samuel Cárdenas | Jorman Copete |
| Suite | Samuel Cárdenas | Juan Montoya |
| Migración | Samuel Cárdenas | Juan Montoya |
| Consultas | Juan Montoya | Willyn Londoño |
| Notificaciones | Samuel Cárdenas | Juan Montoya |
| ICT | Juan Montoya | Willyn Londoño |
| Integraciones | Juan Montoya | Willyn Londoño |
| Log | Juan Montoya | Willyn Londoño |
| Reportes | Daniel Amado | David Chica |
| Ayuda | Daniel Amado | David Chica |
| Doctor FLIT | Daniel Amado | David Chica |
| Plataforma | Jorman Copete | Samuel Cárdenas |

---

## 4. Cómo hacerlo efectivo

1. **Socializar el reparto** con el equipo en una reunión corta: cada persona confirma su rol de principal y de secundario.
2. **CODEOWNERS en GitHub** en cada repositorio, con el principal y el secundario de cada carpeta, para que su revisión sea obligatoria.
3. **Protección de rama** en `develop`, `staging` y `release` exigiendo la revisión de CODEOWNERS.
4. **Ficha de responsables** en cada README (principal, secundario, cómo desplegar, a quién escalar) y una tabla general en `flit-gitops/docs/`.
5. **Traspaso acompañado** para cada secundario nuevo: al menos un despliegue y una guardia con el principal antes de quedar como respaldo.
6. **Revisión trimestral** del reparto (próxima: 2027-01-20).
