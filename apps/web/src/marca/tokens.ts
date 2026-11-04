/* Réplica de flitsas/flit@ee5011eb40682612bbc51697e3d4d242cc3c6944:packages/ui/src/tokens.ts (ATLAS-ADR-0015).
 * No la edites a mano: node scripts/marca/sincronizar.mjs */
/**
 * Tokens de color FLIT derivados de
 * `.claude/skills/flit-design-guardian/references/flit_design_tokens.json`.
 * Usar estos valores en lugar de hex ad hoc (#557EFF, #FF4E00, …).
 */
export const FLIT = {
  text: {
    primary: '#162744',
    secondary: '#59677D',
    muted: '#7D8798',
    brandTitle: '#526FB8',
  },
  brand: {
    cyan: '#4FD4CC',
    blue: '#4F74C9',
    blueDark: '#162744',
  },
  state: {
    success: '#70CF3A',
    active: '#4F74C9',
    warning: '#F05A35',
    danger: '#E43D30',
    draft: '#59677D',
    muted: '#7D8798',
  },
  border: {
    soft: '#DDE5F0',
    input: '#D9DEE8',
    focus: '#4F74C9',
  },
  background: {
    app: '#EAF2FF',
    modal: '#EEF5FF',
    card: '#FFFFFF',
    tableHeader: '#F4F6FA',
  },
  /** CTA primario autorizado (turquesa → azul). */
  gradientPrimary: 'linear-gradient(90deg, #4FD4CC 0%, #4F74C9 100%)',
  /** Azul activo con alpha (hover / tint). */
  blueAlpha: (a: number) => `rgba(79, 116, 201, ${a})`,
  dangerAlpha: (a: number) => `rgba(228, 61, 48, ${a})`,
  warningAlpha: (a: number) => `rgba(240, 90, 53, ${a})`,
  successAlpha: (a: number) => `rgba(112, 207, 58, ${a})`,
} as const;
