private static void SendSMSMessage(Message myMessage)
{
    bool debugMode = IsDebugMode(out string[] debugRecipients);

    List<string> recipients = debugMode ? debugRecipients.ToList() : GetRecipients(myMessage);

    switch (myMessage.SmsGate)
    {
        case MessageSmsGate.ATS:
            SendViaGateway(myMessage, recipients, GlobalEnvironment.ATSLogin, GlobalEnvironment.ATSPassword, "ATS");
            break;

        case MessageSmsGate.Daktela:
            SendViaGateway(myMessage, recipients, GlobalEnvironment.DaktelaLogin, GlobalEnvironment.DaktelaPassword, "Daktela");
            break;

        default:
            SendViaSimGateway(myMessage, recipients);
            break;
    }
}

private static bool IsDebugMode(out string[] debugRecipients)
{
    debugRecipients = Array.Empty<string>();

    if (bool.TryParse(System.Configuration.ConfigurationManager.AppSettings["DebugMode"], out bool debugMode) && debugMode)
    {
        string debugTo = System.Configuration.ConfigurationManager.AppSettings["Messages.GSM.Debug.To"];
        if (!string.IsNullOrEmpty(debugTo))
        {
            debugRecipients = debugTo.Replace(",", ";").Split(';');
            return true;
        }
    }

    return false;
}

private static List<string> GetRecipients(Message message)
{
    var recipients = new List<string> { message.Target };

    if (!string.IsNullOrEmpty(message.TargetCC))
        recipients.Add(message.TargetCC);

    if (!string.IsNullOrEmpty(message.TargetBCC))
        recipients.Add(message.TargetBCC);

    return recipients;
}

private static void SendViaGateway(Message message, List<string> recipients, string login, string password, string gatewayName)
{
    using var service = new GSMConnector.GSM.GSMServiceSoapClient();
    foreach (string recipient in recipients)
    {
        try
        {
            message.DateOfSend = DateTime.Now;
            var response = service.SendSMS(login, password, recipient, message.Body, "", "", "", true, null, null);

            if (response.Result)
            {
                message.ExternalId = response.IdMessage;
                message.Status = MessageStatus.Sending;
                Logger.GetDefaultLogger.Write(LoggerMessageLevel.Info, $"SMS X-Message-ID={message.Id} to {recipient} sent via {gatewayName}.");
            }
            else
            {
                HandleError(message, recipient, response.ResultMessage, gatewayName);
            }
        }
        catch (Exception ex)
        {
            HandleException(message, recipient, ex);
        }

        MessageManager.Save(message, false);
    }
}

private static void SendViaSimGateway(Message message, List<string> recipients)
{
    var simSettings = GetSimSettings(message);
    if (simSettings == null)
    {
        throw new BaseExceptions.NonTypeException("Error sending SMS - No SIM space available.");
    }

    using var service = new GSMConnector.GSM.GSMServiceSoapClient();
    foreach (string recipient in recipients)
    {
        try
        {
            message.DateOfSend = DateTime.Now;
            var response = service.SendSMS(simSettings.Login, simSettings.Password, recipient, message.Body, "", "", "", true, null, null);

            if (response.Result)
            {
                message.ExternalId = response.IdMessage;
                message.Status = MessageStatus.Sending;
                Logger.GetDefaultLogger.Write(LoggerMessageLevel.Info, $"SMS X-Message-ID={message.Id} to {recipient} sent.");
            }
            else
            {
                HandleError(message, recipient, response.ResultMessage, "SIM Gateway");
            }
        }
        catch (Exception ex)
        {
            HandleException(message, recipient, ex);
        }

        MessageManager.Save(message, false);
    }
}

private static SimSettings GetSimSettings(Message message)
{
    return message.Kind == MessageKind.SMSCampaign
        ? SimSettingsManager.GetSimSettingForMessage(message.Environment, SimSettingsType.Campaign)
        : SimSettingsManager.GetSimSettingForMessage(message.Environment, SimSettingsType.Standard);
}

private static void HandleError(Message message, string recipient, string errorMessage, string gatewayName)
{
    message.Status = MessageStatus.Error;
    Logger.GetDefaultLogger.Write(LoggerMessageLevel.Error, $"Error sending SMS X-Message-ID={message.Id} to {recipient} via {gatewayName}. Error: {errorMessage}");
}

private static void HandleException(Message message, string recipient, Exception exception)
{
    string exceptionMessage = exception.ToString();
    if (exceptionMessage.Contains("Nepodarilo se odeslat SMS Ex: Telefon nema nutnych 9 znaku"))
    {
        Logger.GetDefaultLogger.Write(LoggerMessageLevel.Info, $"Message Id={message.Id} to {recipient} - {exceptionMessage}");
        message.Status = MessageStatus.Error;
    }
    else
    {
        throw;
    }
}
