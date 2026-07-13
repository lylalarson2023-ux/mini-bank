using System;
using System.Threading;
using System.Threading.Tasks;

namespace ADN_pay.Services
{
    // Petit « retardateur » (debounce) : n'exécute l'action que si aucun nouvel
    // appel n'arrive pendant le délai. Utile pour les vérifications déclenchées à
    // la frappe (ex. recherche d'un destinataire de virement) — évite de lancer un
    // appel serveur à chaque caractère. Une instance par composant ; penser à
    // Dispose() pour annuler un appel en attente au démontage.
    public sealed class Debouncer : IDisposable
    {
        private CancellationTokenSource? _cts;

        public async Task DebounceAsync(int delayMs, Func<Task> action)
        {
            // Annule l'attente précédente : seule la dernière frappe déclenchera l'action.
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            try
            {
                await Task.Delay(delayMs, token);
                if (!token.IsCancellationRequested)
                    await action();
            }
            catch (TaskCanceledException)
            {
                // Frappe suivante arrivée avant la fin du délai : appel ignoré (normal).
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
