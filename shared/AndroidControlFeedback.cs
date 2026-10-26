namespace Ponte.Core;

public static class AndroidControlFeedback
{
    public static string? Message(string? code) => code switch
    {
        "accessibility_disabled" => "Somente visualização: no telefone, ative Ponte · controle pelo PC na Acessibilidade. Se já estiver ligado, desligue e ligue novamente.",
        "gestures_unavailable" => "O Android não concedeu gestos ao serviço. No telefone, desative/ative a Acessibilidade do Ponte e autorize o controle nas opções oficiais do sistema.",
        "protected_window" => "Esta tela é protegida (Ponte, configurações ou permissões). Use Início/Voltar para sair; cliques e texto permanecem bloqueados aqui.",
        "window_unavailable" => "O Android não informou a janela ativa. Tente Início e abra outro app; se persistir, desative/ative a Acessibilidade do Ponte no telefone.",
        "sharing_inactive" => "Controle pausado: inicie e autorize novamente Compartilhar Android no telefone.",
        "locked" => "Android bloqueado. Desbloqueie o telefone e autorize novamente a captura.",
        "no_editable_field" => "Toque primeiro em um campo editável do aplicativo. Este campo não aceita substituição de texto.",
        "password_field" => "Campo de senha: entrada de texto remota não permitida.",
        "action_rejected" => "O Android recusou ou cancelou a ação. Confira Acessibilidade e se outro gesto está em andamento no telefone.",
        "invalid_command" => "Comando inválido recusado pelo Android.",
        "navigation_sent" => "Navegação aceita pelo Android. Abra um aplicativo para interagir.",
        "gesture_sent" => "Gesto enviado ao Android; aguardando conclusão.",
        "gesture_completed" => "Gesto concluído pelo Android.",
        "text_applied" => "Texto aplicado ao campo focado.",
        "ready" => "Controle ativo · clique para tocar, arraste para deslizar. Início/Voltar também funcionam no Ponte.",
        _ => null
    };
}
