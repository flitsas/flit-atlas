-- Solo para desarrollo local (AppHost de Aspire). En dev/qa/pdn lo ejecuta el administrador de la base
-- (prompt maestro A-06 y §14). El esquema atlas y el usuario flit_atlas llegan en la HU 1.2.
CREATE DATABASE atlas;
\connect atlas
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS unaccent;
