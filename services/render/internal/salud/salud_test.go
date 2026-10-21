package salud

import (
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestSondasResponden(t *testing.T) {
	mux := http.NewServeMux()
	Registrar(mux)

	for _, ruta := range []string{"/health/live", "/health/ready"} {
		rec := httptest.NewRecorder()
		mux.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, ruta, nil))
		if rec.Code != http.StatusOK || rec.Body.String() != "Healthy" {
			t.Errorf("%s: código %d, cuerpo %q", ruta, rec.Code, rec.Body.String())
		}
	}
}
