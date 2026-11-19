# Responsables por producto y por módulo

**Fecha:** 2026-10-20 · **Definió:** Persona Demo Uno (Líder técnico) · **Versión 5:** reparto definido por el líder técnico

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
| **Persona Demo Uno** | Firmas, Sentinel, Atlas, flit-scheduler, Plataforma | Vigía, Administradores, Identidad, Infraestructura |
| **Persona Demo Dos** | Diagnóstico, ICT, Integraciones, Log, Consultas | Comparendos, Suite, Migración, Trámites, Notificaciones |
| **Persona Demo Tres** | Vigía, Infraestructura | Sentinel, Certify, flit-scheduler |
| **Persona Demo Cuatro** | FLITO, People | Usuarios, Reportes, Ayuda, Doctor FLIT |
| **Persona Demo Cinco** | Trámites, Administradores | Diagnóstico, ICT, Integraciones, Log, Consultas |
| **Persona Demo Seis** | Comparendos, Suite, Migración, Identidad, Usuarios, Notificaciones | Atlas, FLITO, People, Plataforma |
| **Persona Demo Séptima** | Certify, Reportes, Ayuda, Doctor FLIT | Firmas |

---

## 3. Reparto por producto y módulo

### 3.1 Productos

| Producto | Principal | Secundario |
|---|---|---|
| Vigía | Persona Demo Tres | Persona Demo Uno |
| Certify | Persona Demo Séptima | Persona Demo Tres |
| FLITO | Persona Demo Cuatro | Persona Demo Seis |
| Firmas | Persona Demo Uno | Persona Demo Séptima |
| Sentinel | Persona Demo Uno | Persona Demo Tres |
| Atlas | Persona Demo Uno | Persona Demo Seis |
| People | Persona Demo Cuatro | Persona Demo Seis |
| flit-scheduler | Persona Demo Uno | Persona Demo Tres |
| Infraestructura | Persona Demo Tres | Persona Demo Uno |

### 3.2 Módulos de FLIT 2

| Módulo | Principal | Secundario |
|---|---|---|
| Trámites | Persona Demo Cinco | Persona Demo Dos |
| Diagnóstico | Persona Demo Dos | Persona Demo Cinco |
| Comparendos | Persona Demo Seis | Persona Demo Dos |
| Administradores | Persona Demo Cinco | Persona Demo Uno |
| Usuarios | Persona Demo Seis | Persona Demo Cuatro |
| Identidad | Persona Demo Seis | Persona Demo Uno |
| Suite | Persona Demo Seis | Persona Demo Dos |
| Migración | Persona Demo Seis | Persona Demo Dos |
| Consultas | Persona Demo Dos | Persona Demo Cinco |
| Notificaciones | Persona Demo Seis | Persona Demo Dos |
| ICT | Persona Demo Dos | Persona Demo Cinco |
| Integraciones | Persona Demo Dos | Persona Demo Cinco |
| Log | Persona Demo Dos | Persona Demo Cinco |
| Reportes | Persona Demo Séptima | Persona Demo Cuatro |
| Ayuda | Persona Demo Séptima | Persona Demo Cuatro |
| Doctor FLIT | Persona Demo Séptima | Persona Demo Cuatro |
| Plataforma | Persona Demo Uno | Persona Demo Seis |

---

## 4. Cómo hacerlo efectivo

1. **Socializar el reparto** con el equipo en una reunión corta: cada persona confirma su rol de principal y de secundario.
2. **CODEOWNERS en GitHub** en cada repositorio, con el principal y el secundario de cada carpeta, para que su revisión sea obligatoria.
3. **Protección de rama** en `develop`, `staging` y `release` exigiendo la revisión de CODEOWNERS.
4. **Ficha de responsables** en cada README (principal, secundario, cómo desplegar, a quién escalar) y una tabla general en `flit-gitops/docs/`.
5. **Traspaso acompañado** para cada secundario nuevo: al menos un despliegue y una guardia con el principal antes de quedar como respaldo.
6. **Revisión trimestral** del reparto (próxima: 2027-01-20).
