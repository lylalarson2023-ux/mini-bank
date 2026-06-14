#r "nuget: Microsoft.Data.Sqlite, 10.0.5"
using Microsoft.Data.Sqlite;

var conn = new SqliteConnection("Data Source=AdnPayData.db");
conn.Open();
var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT Id, Email, IsAdmin, Statut, Solde, PendingPremiumUpgrade, PendingCreditRequest, PendingCreditAmount, Dette FROM UserProfiles;";
var reader = cmd.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"Id: {reader[0]} | Email: {reader[1]} | Admin: {reader[2]} | Statut: {reader[3]} | Solde: {reader[4]} MAD | PendingPremium: {reader[5]} | PendingCredit: {reader[6]} | CreditAmount: {reader[7]} | Dette: {reader[8]}");
}
reader.Close();
conn.Close();
