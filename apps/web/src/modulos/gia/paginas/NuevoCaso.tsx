import { useNavigate } from 'react-router';

import { useEnvio, type CasoDetalle } from '../api';
import { Pagina } from '../componentes';
import { FICHA_VACIA, FormularioCaso } from './EditarCaso';

/** Registrar un caso de uso: nace en borrador. */
export default function NuevoCaso() {
  const navegar = useNavigate();
  const envio = useEnvio<CasoDetalle>();
  return (
    <Pagina titulo="Nuevo caso de uso" descripcion="Nace en borrador. Lo obligatorio se exige al enviarlo a revisión.">
      <FormularioCaso
        inicial={FICHA_VACIA}
        dueno={null}
        conCodigo
        ocupado={envio.isPending}
        error={envio.error}
        alEnviar={(ficha, { codigo }) =>
          envio.mutate({ ruta: '/casos', cuerpo: { codigo, ficha } }, { onSuccess: (detalle) => void navegar(`/gobierno-ia/casos/${detalle.caso.id}`) })
        }
      />
    </Pagina>
  );
}
