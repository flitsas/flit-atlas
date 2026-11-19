import {
  Bell,
  BookOpen,
  Boxes,
  Building2,
  CalendarDays,
  Ellipsis,
  FileCode2,
  Hand,
  House,
  type LucideProps,
  Package,
  Search,
  Settings,
  ShieldCheck,
  Sparkles,
  Users,
} from 'lucide-react';
import type { ComponentType } from 'react';

import type { NombreIcono } from '../modulos/contrato';

/** Único lugar que traduce el nombre de ícono de un manifiesto al componente de lucide (se empaquetan solo estos). */
const ICONOS: Record<NombreIcono, ComponentType<LucideProps>> = {
  inicio: House,
  buscar: Search,
  mas: Ellipsis,
  catalogo: Boxes,
  equipo: Users,
  calendario: CalendarDays,
  contratos: FileCode2,
  documentos: BookOpen,
  asistente: Sparkles,
  notificaciones: Bell,
  ajustes: Settings,
  permisos: ShieldCheck,
  ejemplo: Hand,
  administracion: Building2,
  portal: Package,
};

export function Icono({ nombre, ...props }: { nombre: NombreIcono } & LucideProps) {
  const Componente = ICONOS[nombre];
  return <Componente aria-hidden="true" focusable="false" {...props} />;
}
