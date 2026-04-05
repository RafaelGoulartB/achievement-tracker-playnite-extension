# AchievementTracker: Theme Integration Handover Doc

## 🎯 Objetivo
Integrar o sistema de Achievements (Conquistas) localmente na página de detalhes do Playnite, especificamente para o tema **FusionX**. O objetivo é que o usuário não precise abrir uma janela separada, mas veja as conquistas diretamente nas colunas de detalhes (GridView e DetailsView).

---

## 🛠️ Estado Atual
- **Painel no Tema**: O código XAML foi inserido com sucesso em `DetailsViewGameOverview.xaml` e `GridViewGameOverview.xaml`. O texto "ACHIEVEMENTS" aparece, mas a caixa está vazia.
- **Plugin Carregado**: O plugin está sendo carregado pelo Playnite (os menus de contexto aparecem e funcionam).
- **Problema de Injeção**: O método `GetGameViewControl` no C# parece não estar sendo chamado pelo Playnite para injetar o componente `AchievementTrackerControl` dentro do `ContentControl` do tema.

---

## 📂 Arquivos Chave (Contexto)

1.  **Extensão (C#):**
    - `c:\Games\_PLAYNITE\Extensions\AchievementTracker\src\AchievementTrackerPlugin.cs`: Lógica de injeção (`GetGameViewControl`).
    - `c:\Games\_PLAYNITE\Extensions\AchievementTracker\src\UI\AchievementTrackerControl.xaml.cs`: Componente que deveria ser injetado.
    - `c:\Games\_PLAYNITE\Extensions\AchievementTracker\src\Properties\AssemblyInfo.cs`: Contém o GUID do projeto (unificado).

2.  **Configuração:**
    - `c:\Games\_PLAYNITE\Extensions\AchievementTracker\extension.yaml`: Definição do ID (`11ab4f7c-389f-43e5-9f5b-11c5d9a911ab`) e Nome (`AchievementTracker`).

3.  **Tema (XAML):**
    - `c:\Games\_PLAYNITE\Themes\Desktop\FusionX_...\Views\DetailsViewGameOverview.xaml`: Ponto de injeção usando `<ContentControl x:Name="AchievementTracker_MainControl" />`.

---

## 🔍 Descobertas Importantes (O Padrão HLTB)
Estudamos o plugin `HowLongToBeat` (HLTB) e descobrimos as seguintes regras para injeção nativa:

1.  **GUID Unificado**: O GUID deve ser IDENTICO em:
    - O campo `Id` no `extension.yaml`.
    - O atributo `[assembly: Guid(...)]` no `AssemblyInfo.cs`.
    - A propriedade `public override Guid Id` no código C#.

2.  **Naming Convention**: Se o plugin se chama `AchievementTracker`, o Playnite espera que o controle no tema tenha o prefixo `AchievementTracker_`. Ex: `AchievementTracker_MainControl`.

3.  **Prefix Stripping**: Quando o Playnite pergunta ao plugin pelo controle, ele passa apenas o sufixo. Ou seja, em `GetGameViewControl`, o `args.Name` recebido será apenas `"MainControl"`, mesmo que no tema seja `AchievementTracker_MainControl`.

---

## 🚩 Próximos Passos para Debug
- **Referência HLTB**: O usuário forneceu o código do HLTB para comparação em: `c:\Users\rafael\Apps\Dev\playnite-howlongtobeat-plugin\source\HowLongToBeat.cs`.

---

## ⚠️ Observação Técnica
O projeto usa o novo formato de `.csproj` (SDK Style), mas forçamos o uso de um `AssemblyInfo.cs` manual desativando a geração automática (`<GenerateAssemblyInfo>false</GenerateAssemblyInfo>`).
