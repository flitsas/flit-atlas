# Generador de snapshots de despliegue

HU 6.4 ([ATLAS-ADR-0056](../../docs/adr/0056-arquitectura-likec4.md) §5). Lee un checkout de `flit-gitops` y escribe un snapshot JSON por ambiente y clúster, versionado por el sha de gitops, más el modelo de despliegue en DSL de LikeC4 si le das el modelo lógico. Vive aquí, y `flit-gitops` lo invoca con el workflow reutilizable [`atlas-publicar-arquitectura.yml`](../../.github/workflows/atlas-publicar-arquitectura.yml).

```bash
npm ci
node gen.mjs --repo <flit-gitops> --sha <commit> --salida gen/ [--modelo modelo.json] [--kustomize "kustomize build"]
```

La salida trae las imágenes y los hosts públicos: **no la commitees**, solo viaja a Atlas.

## Qué sale y qué no

Se lee por lista blanca. De un manifiesto solo salen `kind`, nombre, réplicas, imagen con su digest y hosts de Ingress. Nunca se abren `Secret`, `SealedSecret`, `ConfigMap` ni `EndpointSlice`, ni se miran variables de entorno, argumentos, volúmenes o anotaciones.

| Si encuentra | Hace |
|---|---|
| Un host de Ingress que es una IP o termina en `.svc`, `.local`, `.internal`, `.lan`, `.corp`, `.home` | Lo quita y avisa cuántos, sin nombrarlos |
| Una imagen en un registro con puerto, IP o nombre interno | Deja solo la ruta de la imagen, sin el registro |
| Algo con forma de IP o de secreto en la salida final | **Se detiene** y nombra el campo, nunca el valor |
| Un namespace sin ambiente o una raíz sin clústeres en la configuración | Lo salta y avisa |
| Nada que generar | Falla: un generador vacío no debe parecer un éxito |

atlas-api vuelve a revisar todo al recibirlo y rechaza con 422 un campo desconocido, una IP o un host interno.

## La configuración

Vive en `flit-gitops`, en `arquitectura-gen.yaml`: este repo no guarda nombres reales. La forma está en [`fixtures/gitops-demo/arquitectura-gen.yaml`](fixtures/gitops-demo/arquitectura-gen.yaml).

| Clave | Qué es |
|---|---|
| `raices` | Por cada Application de `bootstrap/`, los clústeres donde corre |
| `ambientes` | Por cada namespace, su ambiente (`dev`, `qa` o `pdn`) |
| `fueraDeK3s` | Servicios declarados a mano: `nombre`, `ambiente`, `cluster`, `metodo`, `elementoModelo`, `verificadoEn` |
| `ignorar` | Lo que el drift no debe señalar: `patron`, `motivo`, `revisarEn` (`AAAA-MM-DD`). Vencida la fecha, deja de aplicar |

## Pruebas

```bash
node --test
```

Las que renderizan necesitan `kubectl kustomize`, y se saltan si no está. Los fixtures son ficticios y traen a propósito secretos, IP de ejemplo, hosts internos y un `EndpointSlice` para comprobar que no salen.

`fixtures/modelo-demo/` y `fixtures/modelo-grande/` son modelos ficticios. Sus `*.export.json` son la salida real de `likec4 export json --project <nombre>` con LikeC4 1.59.4, y los usan las pruebas de la API, Vitest y Playwright.
