using System;
using MBANK_ETUDIANT.Models;

namespace MBANK_ETUDIANT.Services
{
    public class NotificationService
    {
        // Événement auquel les composants UI (comme MainLayout) vont s'abonner
        public event Action<Notification>? OnNotificationReceived;

        /// <summary>
        /// Envoie une notification à l'interface utilisateur.
        /// </summary>
        /// <param name="message">Le texte à afficher</param>
        /// <param name="type">SUCCESS, ERROR, ou INFO</param>
        public void Notify(string message, NotificationType type = NotificationType.INFO)
        {
            var notif = new Notification 
            { 
                Message = message, 
                Type = type,
                Timestamp = DateTime.Now 
            };

            // On déclenche l'événement pour informer l'UI
            OnNotificationReceived?.Invoke(notif);
        }

        // Méthodes d'aide rapides pour simplifier l'appel dans le code
        public void Success(string message) => Notify(message, NotificationType.SUCCESS);
        public void Error(string message) => Notify(message, NotificationType.ERROR);
        public void Info(string message) => Notify(message, NotificationType.INFO);
    }
}