# PR propuestos para los repos legados

Generados por `python -m atlas_docs.legado` (HU 3.8, ATLAS-ADR-0037). **No se abren solos**: abrir un PR en un repo
legado requiere el permiso del líder técnico.

| Repo | Contrato | Versión | Archivos |
|---|---|---|---|
| `BackApiExternalAuth` | `external-auth` | 1.5.0 | [`tsoa.json`](BackApiExternalAuth/tsoa.json) · [`atlas-publicar.yml`](BackApiExternalAuth/.github/workflows/atlas-publicar.yml) |
| `BackApiExternalTransact` | `external-transact` | 3.0.0 | [`tsoa.json`](BackApiExternalTransact/tsoa.json) · [`atlas-publicar.yml`](BackApiExternalTransact/.github/workflows/atlas-publicar.yml) |
| `BackApiExternalTransactAttach` | `external-transactattach` | 1.8.0 | [`tsoa.json`](BackApiExternalTransactAttach/tsoa.json) · [`atlas-publicar.yml`](BackApiExternalTransactAttach/.github/workflows/atlas-publicar.yml) |
| `BackApiExternalTransactStatus` | `external-transactstatus` | 1.7.0 | [`tsoa.json`](BackApiExternalTransactStatus/tsoa.json) · [`atlas-publicar.yml`](BackApiExternalTransactStatus/.github/workflows/atlas-publicar.yml) |
| `BackApiExternalTransactWebHook` | `external-transactwebhook` | 1.0.0 | [`tsoa.json`](BackApiExternalTransactWebHook/tsoa.json) · [`atlas-publicar.yml`](BackApiExternalTransactWebHook/.github/workflows/atlas-publicar.yml) |
| `BackApiExternalAttachments` | `external-attachments` | 1.0.0 | [`tsoa.json`](BackApiExternalAttachments/tsoa.json) · [`atlas-publicar.yml`](BackApiExternalAttachments/.github/workflows/atlas-publicar.yml) |
| `BackApiExternalLog` | `external-log` | 1.0.0 | [`tsoa.json`](BackApiExternalLog/tsoa.json) · [`atlas-publicar.yml`](BackApiExternalLog/.github/workflows/atlas-publicar.yml) |
| `BackApiExternalTransactValiQueryExt` | `external-valiqueryext` | 1.0.0 | [`tsoa.json`](BackApiExternalTransactValiQueryExt/tsoa.json) · [`atlas-publicar.yml`](BackApiExternalTransactValiQueryExt/.github/workflows/atlas-publicar.yml) |

## Qué cambia en cada repo

1. `tsoa.json`: título del servicio en lugar de `api_express_typescript_boilerplate`, versión semántica tomada del
   Excel de contratos ICT, descripción y un contacto que no es una persona. El swagger que genera tsoa queda igual en
   lo demás.
2. `contracts/openapi/<contrato>.yaml`: copia del spec enriquecido de `contracts/legado/` de flit-atlas.
3. `.github/workflows/atlas-publicar.yml`: llama al workflow reutilizable de la HU 3.3 con el aplicativo del catálogo
   y la ruta del spec.

## Pendientes

- [por verificar] Nombre y versión fijada del workflow reutilizable (HU 3.3) y la variable `ATLAS_URL`.
- [por verificar] Que los repos privados de la organización puedan usar workflows reutilizables de `flit-atlas`
  (ajuste de acceso de Actions en el repo `flit-atlas`).
- [por verificar] Si el líder prefiere un repo de contratos legados, el workflow vive allí y no en cada repo.
