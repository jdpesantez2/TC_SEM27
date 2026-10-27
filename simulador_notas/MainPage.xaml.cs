using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace simulador_notas;

public partial class MainPage : ContentPage
{
    private static readonly Color Verde = Color.FromArgb("#16A34A");
    private static readonly Color Ambar = Color.FromArgb("#D97706");
    private static readonly Color Rojo = Color.FromArgb("#DC2626");
    private static readonly Color Neutro = Color.FromArgb("#64748B");

    private const int MaxComponentes = 8;
    private const string ClaveEstado = "estado_v2";
    private const string ClaveTema = "tema_v2";

    private readonly ObservableCollection<Componente> _componentes = new();

    private bool _cargando;
    private double _scoreActual;
    private double _progreso;
    private double _minimaValor = 7;
    private Color _colorBarra = Neutro;
    private string _ultimoChip = "";
    private string _resumen = "";

    public MainPage()
    {
        InitializeComponent();

        BindableLayout.SetItemsSource(ComponentesLayout, _componentes);

        AplicarTemaGuardado();
        Cargar();
        Recalcular(false);
    }

    // ---------- Ancho máximo centrado ----------
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
            Raiz.WidthRequest = Math.Min(width, 720);
    }

    // ---------- Tema claro / oscuro ----------
    private void AplicarTemaGuardado()
    {
        if (Application.Current is not { } app) return;

        var guardado = Preferences.Default.Get(ClaveTema, "");
        if (guardado == "dark") app.UserAppTheme = AppTheme.Dark;
        else if (guardado == "light") app.UserAppTheme = AppTheme.Light;

        BtnTema.Text = app.RequestedTheme == AppTheme.Dark ? "☀️" : "🌙";
    }

    private void OnTemaClicked(object? sender, EventArgs e)
    {
        if (Application.Current is not { } app) return;

        bool eraOscuro = app.RequestedTheme == AppTheme.Dark;
        app.UserAppTheme = eraOscuro ? AppTheme.Light : AppTheme.Dark;
        BtnTema.Text = eraOscuro ? "🌙" : "☀️";
        Preferences.Default.Set(ClaveTema, eraOscuro ? "light" : "dark");
    }

    // ---------- Componentes ----------
    private void Agregar(Componente c)
    {
        c.Cambiado += OnDatoCambiado;
        _componentes.Add(c);
    }

    private void CargarPorDefecto()
    {
        _componentes.Clear();
        MateriaEntry.Text = string.Empty;
        MinimaEntry.Text = "7";
        Agregar(new Componente { Nombre = "Parcial 1", Peso = "30" });
        Agregar(new Componente { Nombre = "Parcial 2", Peso = "30" });
        Agregar(new Componente { Nombre = "Examen final", Peso = "40" });
    }

    private async void OnAgregarClicked(object? sender, EventArgs e)
    {
        if (_componentes.Count >= MaxComponentes)
        {
            await DisplayAlertAsync("Límite alcanzado", $"Puedes tener hasta {MaxComponentes} componentes.", "OK");
            return;
        }

        Agregar(new Componente { Nombre = $"Componente {_componentes.Count + 1}" });
        OnDatoCambiado();
    }

    private async void OnEliminarClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Componente c }) return;

        if (_componentes.Count <= 1)
        {
            await DisplayAlertAsync("Aviso", "Debe existir al menos un componente.", "OK");
            return;
        }

        c.Cambiado -= OnDatoCambiado;
        _componentes.Remove(c);
        OnDatoCambiado();
    }

    private async void OnReiniciarClicked(object? sender, EventArgs e)
    {
        bool confirmar = await DisplayAlertAsync("Reiniciar", "¿Restablecer todos los datos?", "Sí, reiniciar", "Cancelar");
        if (!confirmar) return;

        _cargando = true;
        CargarPorDefecto();
        _cargando = false;

        Recalcular();
        Guardar();
    }

    private async void OnCompartirClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_resumen)) return;

        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Simulador de notas",
                Text = _resumen
            });
        }
        catch
        {
            await DisplayAlertAsync("Compartir", "No se pudo abrir el menú de compartir.", "OK");
        }
    }

    // ---------- Eventos de cambio ----------
    private void OnTextoCambiado(object? sender, TextChangedEventArgs e) => OnDatoCambiado();

    private void OnDatoCambiado()
    {
        if (_cargando) return;
        Recalcular();
        Guardar();
    }

    private void OnPistaSizeChanged(object? sender, EventArgs e) => ActualizarBarra(false);

    // ---------- Guardado local ----------
    private void Guardar()
    {
        try
        {
            var estado = new EstadoGuardado(
                MateriaEntry.Text ?? "",
                MinimaEntry.Text ?? "7",
                _componentes.Select(c => new ComponenteDto(c.Nombre, c.Peso, c.Nota)).ToList());

            Preferences.Default.Set(ClaveEstado, JsonSerializer.Serialize(estado));
        }
        catch { /* si falla, no pasa nada */ }
    }

    private void Cargar()
    {
        _cargando = true;
        try
        {
            var json = Preferences.Default.Get(ClaveEstado, string.Empty);
            var estado = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<EstadoGuardado>(json);

            if (estado is { Componentes.Count: > 0 })
            {
                MateriaEntry.Text = estado.Materia ?? "";
                MinimaEntry.Text = string.IsNullOrWhiteSpace(estado.Minima) ? "7" : estado.Minima;
                foreach (var d in estado.Componentes)
                    Agregar(new Componente { Nombre = d.Nombre, Peso = d.Peso, Nota = d.Nota });
            }
            else
            {
                CargarPorDefecto();
            }
        }
        catch
        {
            CargarPorDefecto();
        }
        finally
        {
            _cargando = false;
        }
    }

    // ---------- Lectura de números ----------
    // Vacío => (true, null). Texto inválido => (false, null).
    private static (bool ok, double? valor) LeerNumero(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return (true, null);

        texto = texto.Trim().Replace(',', '.');

        if (double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
            return (true, n);

        return (false, null);
    }

    private static string NombreDe(Componente c, int indice) =>
        string.IsNullOrWhiteSpace(c.Nombre) ? $"componente {indice + 1}" : c.Nombre.Trim();

    // ---------- Cálculo principal ----------
    private void Recalcular(bool animar = true)
    {
        MateriaResultadoLabel.Text = string.IsNullOrWhiteSpace(MateriaEntry.Text)
            ? "RESULTADO"
            : MateriaEntry.Text.Trim().ToUpper();

        // Nota mínima
        var (okMin, minimaN) = LeerNumero(MinimaEntry.Text);
        if (!okMin || minimaN is null || minimaN < 0 || minimaN > 10)
        {
            Aplicar("Revisar datos", "La nota mínima debe ser un número entre 0 y 10.", Ambar, null, "—", "—", animar);
            return;
        }

        double minima = minimaN.Value;
        _minimaValor = minima;
        MinimaTileLabel.Text = minima.ToString("0.##");
        MinimaLegend.Text = $"Mínima: {minima:0.##}";

        var lista = _componentes.ToList();
        int n = lista.Count;

        // Pesos
        var pesos = new double[n];
        double sumaPesos = 0;
        for (int i = 0; i < n; i++)
        {
            var (ok, v) = LeerNumero(lista[i].Peso);
            if (!ok)
            {
                Aplicar("Revisar datos", "Los pesos deben ser números.", Ambar, null, "—", "—", animar);
                return;
            }
            if (v is null)
            {
                Aplicar("Completa los datos", $"Falta el peso de «{NombreDe(lista[i], i)}».", Neutro, null, "—", "—", animar);
                return;
            }
            if (v < 0 || v > 100)
            {
                Aplicar("Revisar datos", "Cada peso debe estar entre 0 y 100.", Ambar, null, "—", "—", animar);
                return;
            }
            pesos[i] = v.Value;
            sumaPesos += v.Value;
        }

        if (Math.Abs(sumaPesos - 100) > 0.001)
        {
            Aplicar("Revisar pesos",
                $"Los pesos suman {sumaPesos:0.##}%. Deben sumar exactamente 100%.",
                Ambar, null, $"{sumaPesos:0.##}%", "—", animar);
            return;
        }

        // Notas
        var notas = new double?[n];
        for (int i = 0; i < n; i++)
        {
            var (ok, v) = LeerNumero(lista[i].Nota);
            if (!ok)
            {
                Aplicar("Revisar datos", $"La nota de «{NombreDe(lista[i], i)}» debe ser un número.", Ambar, null, "—", "—", animar);
                return;
            }
            if (v is < 0 or > 10)
            {
                Aplicar("Revisar datos", $"La nota de «{NombreDe(lista[i], i)}» debe estar entre 0 y 10.", Rojo, null, "—", "—", animar);
                return;
            }
            notas[i] = v;
        }

        // Acumulado y pendiente
        double acumulado = 0, pesoPendiente = 0;
        var pendientes = new List<string>();
        for (int i = 0; i < n; i++)
        {
            if (notas[i] is double nota)
                acumulado += nota * pesos[i] / 100;
            else if (pesos[i] > 0)
            {
                pesoPendiente += pesos[i];
                pendientes.Add(NombreDe(lista[i], i));
            }
        }

        double fraccionPendiente = pesoPendiente / 100;
        string pendienteTxt = $"{pesoPendiente:0.##}%";
        string listaPend = string.Join(", ", pendientes);

        // Sin nada pendiente: resultado final
        if (fraccionPendiente <= 1e-9)
        {
            if (acumulado >= minima)
            {
                Aplicar("Aprobado",
                    $"¡Felicitaciones! Cerraste con {acumulado:F2}, sobre la mínima de {minima:0.##}.",
                    Verde, acumulado, "0%", "✓", animar);
            }
            else
            {
                Aplicar("Reprobado",
                    $"Cerraste con {acumulado:F2}; la mínima es {minima:0.##}. Te faltaron {(minima - acumulado):F2} puntos.",
                    Rojo, acumulado, "0%", "—", animar);
            }
            return;
        }

        // Hay notas pendientes: simular
        double necesaria = (minima - acumulado) / fraccionPendiente;

        if (necesaria <= 0)
        {
            Aplicar("Aprobado",
                $"¡Ya aprobaste! Aunque saques 0 en {listaPend}, superas la mínima.",
                Verde, acumulado, pendienteTxt, "✓", animar);
        }
        else if (necesaria > 10)
        {
            double maximo = acumulado + 10 * fraccionPendiente;
            Aplicar("Imposible",
                $"Necesitarías {necesaria:F2}/10 en {listaPend}. Con 10 llegarías a {maximo:F2}.",
                Rojo, acumulado, pendienteTxt, necesaria.ToString("F2"), animar);
        }
        else if (necesaria <= 6)
        {
            Aplicar("Vas bien",
                $"Necesitas {necesaria:F2}/10 en {listaPend} para llegar a {minima:0.##}.",
                Verde, acumulado, pendienteTxt, necesaria.ToString("F2"), animar);
        }
        else if (necesaria <= 8.5)
        {
            Aplicar("En juego",
                $"Necesitas un promedio de {necesaria:F2}/10 en {listaPend} para llegar a {minima:0.##}.",
                Ambar, acumulado, pendienteTxt, necesaria.ToString("F2"), animar);
        }
        else
        {
            Aplicar("Difícil",
                $"Necesitas {necesaria:F2}/10 en {listaPend}. ¡Hay que dar el máximo!",
                Rojo, acumulado, pendienteTxt, necesaria.ToString("F2"), animar);
        }
    }

    // ---------- Pintar resultado ----------
    private void Aplicar(string chip, string mensaje, Color color, double? acumulado,
                         string pendiente, string necesita, bool animar)
    {
        ChipLabel.Text = chip;
        ChipBorder.BackgroundColor = color;

        BannerBorder.BackgroundColor = color.WithAlpha(0.13f);
        BannerBorder.Stroke = new SolidColorBrush(color.WithAlpha(0.55f));
        MensajeLabel.Text = mensaje;
        MensajeLabel.TextColor = color;

        PromedioLabel.TextColor = color;
        PendienteLabel.Text = pendiente;
        NecesitaLabel.Text = necesita;

        // Puntaje con animación de conteo
        this.AbortAnimation("score");
        if (acumulado is double destino)
        {
            if (animar)
            {
                double desde = _scoreActual;
                new Animation(v =>
                {
                    _scoreActual = v;
                    PromedioLabel.Text = v.ToString("F2");
                }, desde, destino, Easing.CubicOut)
                    .Commit(this, "score", 16, 450);
            }
            else
            {
                _scoreActual = destino;
                PromedioLabel.Text = destino.ToString("F2");
            }
        }
        else
        {
            _scoreActual = 0;
            PromedioLabel.Text = "--";
        }

        // Barra
        _progreso = acumulado is double a ? Math.Clamp(a / 10, 0, 1) : 0;
        _colorBarra = color;
        Relleno.BackgroundColor = color;
        ActualizarBarra(animar);

        // Resumen para compartir
        string materia = string.IsNullOrWhiteSpace(MateriaEntry.Text) ? "Mi materia" : MateriaEntry.Text.Trim();
        _resumen = acumulado is double ac
            ? $"📚 {materia}\nPuntos acumulados: {ac:F2}/10\n{chip}: {mensaje}"
            : $"📚 {materia}\n{chip}: {mensaje}";

        // Pequeña animación al cambiar de estado
        if (chip != _ultimoChip)
        {
            _ultimoChip = chip;
            if (animar) _ = PulsoAsync();
        }
    }

    private void ActualizarBarra(bool animar)
    {
        double ancho = PistaGrid.Width;
        if (ancho <= 0) return;

        Marcador.TranslationX = Math.Clamp(ancho * _minimaValor / 10 - 1.5, 0, Math.Max(0, ancho - 3));

        double destino = ancho * _progreso;

        this.AbortAnimation("barra");
        if (animar)
        {
            double desde = Math.Max(0, Relleno.WidthRequest);
            new Animation(v => Relleno.WidthRequest = v, desde, destino, Easing.CubicOut)
                .Commit(this, "barra", 16, 500);
        }
        else
        {
            Relleno.WidthRequest = destino;
        }
    }

    private async Task PulsoAsync()
    {
        try
        {
            BannerBorder.Opacity = 0.35;
            _ = BannerBorder.FadeToAsync(1, 280, Easing.CubicOut);
            await ChipBorder.ScaleToAsync(1.14, 110, Easing.CubicOut);
            await ChipBorder.ScaleToAsync(1.0, 150, Easing.CubicIn);
        }
        catch { /* animación opcional */ }
    }
}