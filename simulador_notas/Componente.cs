using System.ComponentModel;

namespace simulador_notas;

public class Componente : INotifyPropertyChanged
{
    private string _nombre = "";
    private string _peso = "";
    private string _nota = "";

    public string Nombre
    {
        get => _nombre;
        set { value ??= ""; if (_nombre == value) return; _nombre = value; Notificar(nameof(Nombre)); }
    }

    public string Peso
    {
        get => _peso;
        set { value ??= ""; if (_peso == value) return; _peso = value; Notificar(nameof(Peso)); }
    }

    public string Nota
    {
        get => _nota;
        set { value ??= ""; if (_nota == value) return; _nota = value; Notificar(nameof(Nota)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Cambiado;

    private void Notificar(string propiedad)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
        Cambiado?.Invoke();
    }
}

public record ComponenteDto(string Nombre, string Peso, string Nota);

public record EstadoGuardado(string Materia, string Minima, List<ComponenteDto> Componentes);