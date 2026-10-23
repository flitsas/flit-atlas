#!/usr/bin/env node
// Genera un par de llaves VAPID (P-256, base64url) para Web Push (HU 1.6, ATLAS-ADR-0020) con la criptografía de Node.
// La privada es secreto: va a los user secrets del AppHost (local) o a un SealedSecret (qa y pdn); nunca al repo.
//
//   node scripts/generar-vapid.mjs
import { generateKeyPairSync } from 'node:crypto';

const { publicKey, privateKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
const jwkPublica = publicKey.export({ format: 'jwk' });
const jwkPrivada = privateKey.export({ format: 'jwk' });
const bytes = (base64url) => Buffer.from(base64url, 'base64url');

const publica = Buffer.concat([Buffer.from([0x04]), bytes(jwkPublica.x), bytes(jwkPublica.y)]).toString('base64url');
const privada = bytes(jwkPrivada.d).toString('base64url');

console.log(`Plataforma__Push__ClavePublica=${publica}`);
console.log(`Plataforma__Push__ClavePrivada=${privada}`);
console.log('');
console.log('En local:');
console.log(`  dotnet user-secrets set "Vapid:ClavePublica" "${publica}" --project aspire/Flit.Atlas.AppHost`);
console.log('  dotnet user-secrets set "Parameters:vapid-clave-privada" "<la clave privada de arriba>" --project aspire/Flit.Atlas.AppHost');
