# Wireframes PWA v1

> Status: 🔒 FROZEN v1 — baseline funcional e visual aprovada para implementação
> Data: 2026-09-17

## 1. Objetivo

Definir a experiência mobile-first da PWA usada pelo proprietário/gestor do estabelecimento. O cliente final opera pelo WhatsApp, sem necessidade de instalar aplicativo ou manter conta administrativa.

A identidade visual, nome e logo do produto ainda não estão definidos. Os wireframes usam identidade neutra até decisão futura de branding. O mockup consolidado aprovado em 2026-09-17 é referência visual da baseline; detalhes cosméticos podem evoluir sem alterar os contratos funcionais congelados neste documento.

## 2. Telas principais aprovadas

1. Onboarding
2. Início / Dashboard
3. Agenda
4. Financeiro
5. Serviços
6. Clientes
7. Configurações

Layout clean, mobile-first, cards de leitura rápida, navegação simples e foco nas informações que exigem atenção do proprietário.

## 3. Onboarding

Fluxo conceitual:

`Criação da conta -> Verificação de e-mail -> Verificação de WhatsApp -> Dados do negócio -> Serviços e preços -> Profissionais e horários -> Conexão WhatsApp do negócio -> Meios de pagamento -> Teste -> Ativação`

A experiência deve evitar aparência de ERP e apresentar a configuração como configuração/treinamento do funcionário virtual.

### 3.1 Segurança no primeiro acesso — aprovado

O Customer (assinante/proprietário) informa e-mail e número de WhatsApp no cadastro. Antes da ativação da conta, ambos devem ser verificados.

Fluxo:

`Customer cria conta -> verifica e-mail -> verifica WhatsApp por OTP -> canais verificados -> conta ativada -> onboarding`

A interface deve registrar os estados de verificação, conceitualmente `EmailVerifiedAt` e `PhoneVerifiedAt`, sem expor detalhes técnicos ao usuário.

A validação inicial dos dois canais não significa exigir dois códigos em todo login. Autenticação reforçada deve poder ser solicitada em situações sensíveis, como novo dispositivo, recuperação de conta, alteração de e-mail/telefone ou operações administrativas críticas.

Códigos OTP não podem ser armazenados em texto puro nem aparecer em logs. O mecanismo de autenticação/verificação deve preferencialmente ser delegado a um provedor de identidade confiável, evitando implementação criptográfica própria.

## 4. Início / Dashboard

Prioriza faturamento realizado, agendamentos, receita prevista, ticket médio, próximos atendimentos, acompanhamentos automáticos e acesso conversacional ao funcionário virtual.

Pagamento pendente normal é acompanhamento, não ação manual do proprietário.

Exemplo:

```text
1 pagamento pendente
João • Corte + Barba • 14:00
Reserva expira em 6 min.
```

A área `Precisa de ação` fica reservada a exceções que realmente exigem decisão/intervenção humana.

## 5. Agenda

Apresenta horário, nome do cliente, serviço/serviços, valor e status. Fotos de clientes ficam fora do MVP. A interface permite filtros por data e profissional e distingue confirmado, aguardando pagamento e horário disponível.

## 6. Pagamento pendente

Todos os agendamentos comerciais exigem pagamento integral para confirmação:

`Appointment PENDING -> reserva temporária -> pagamento integral -> webhook do provider -> Payment CONFIRMED -> Appointment CONFIRMED`

`reservationExpiresAt` governa a reserva. Sem confirmação até o prazo:

`PENDING -> EXPIRED -> horário liberado automaticamente`

O texto `cliente não confirmou o pagamento` não deve ser usado; a confirmação financeira é responsabilidade do provider/webhook.

## 7. Financeiro

Prioriza recebido, previsto, estornos, agendamentos pagos, ticket médio, métodos de pagamento, filtros por período e relatório/exportação quando aplicável.

Métodos do serviço no MVP: PIX, cartão de crédito e cartão de débito. Não existe sinal, adiantamento, pagamento parcial ou boleto.

## 8. Serviços

Lista simples com nome, preço, duração, tipo Serviço/Combo, status e profissionais habilitados.

Cadastro/edição usa linguagem do estabelecimento. Detalhes técnicos como `LocationService` e `ProfessionalService` não são expostos. Em cenário de uma única Location ativa, a unidade pode ser inferida pela UX.

Combo permite selecionar apenas componentes SINGLE. Combo não contém Combo; a própria interface não oferece combos como componentes e o backend preserva a regra.

## 9. Clientes

Client (cliente do estabelecimento) mantém somente os dados necessários ao atendimento: nome, WhatsApp e observação opcional. Não há e-mail no cadastro do Client no MVP.

Toda interação operacional com o Client ocorre via WhatsApp: atendimento, agendamento, confirmação, pagamento, lembrete, reagendamento, cancelamento e avisos.

A tela pode apresentar última visita, quantidade de atendimentos, valor histórico realizado e inatividade derivada da última visita `COMPLETED`. Não persistir `IsInactive`.

Customer (assinante da plataforma) é diferente de Client e mantém e-mail e WhatsApp em seu perfil para identidade, segurança, acesso e billing da plataforma.

## 10. Configurações

Estrutura aprovada:

- Meu perfil
- Meu negócio
- Unidade
- Horários
- Agendamentos
- Pagamentos
- WhatsApp
- Funcionário virtual
- Notificações
- Minha assinatura
- Ajuda

### 10.1 Meu perfil / Segurança e acesso

Exibe e-mail e WhatsApp do Customer com estado de verificação, além de acesso às funções de segurança.

Exemplo conceitual:

```text
Segurança e acesso
────────────────────────────
E-mail
mad***@gmail.com       ✓ Verificado

WhatsApp
(11) *****-1234       ✓ Verificado

Verificação em duas etapas
● Ativa

Dispositivos conectados       ›
Alterar senha                  ›
```

Informações sensíveis devem ser mascaradas na interface quando apropriado.

### 10.2 Horários e agendamentos

A UX fala em horários, antecedência, intervalo dos horários e política de cancelamento. Conceitos internos como `AvailabilityRule` e estados técnicos permanecem no backend.

### 10.3 Pagamentos

Mostra conexão do gateway e meios habilitados. Não armazena/exibe dados brutos de cartão e não oferece configuração de sinal ou pagamento parcial.

### 10.4 WhatsApp e Funcionário virtual

WhatsApp mostra conexão, número e saúde do canal. Funcionário virtual configura comportamento de atendimento dentro das permissões e regras do backend. A IA nunca ganha autoridade financeira ou de domínio por configuração de interface.

### 10.5 Minha assinatura

Representa SaaS Billing da plataforma e permanece separado dos pagamentos dos Clients ao estabelecimento.

## 11. Fotos e privacidade

Client não depende de foto para Agenda ou Dashboard. O MVP não cria upload/Blob Storage de foto de Client apenas para UI. A integração do WhatsApp não é fonte arquitetural de foto do Client.

## 12. Princípios consolidados

- mobile-first;
- Client final no WhatsApp;
- Customer na PWA + WhatsApp administrativo;
- e-mail somente para perfil/identidade/segurança/billing do Customer;
- verificação obrigatória de e-mail + WhatsApp do Customer no primeiro acesso;
- autenticação reforçada para situações sensíveis;
- dados essenciais antes de elementos decorativos;
- nenhuma dependência de foto de Client;
- pagamento pendente é acompanhamento automático;
- alertas críticos somente quando ação humana for necessária;
- identidade visual/nome/logo definidos posteriormente;
- simplicidade e automação antes de amplitude de ERP.

## 13. Freeze v1

Esta baseline está congelada para o primeiro ciclo de implementação. Alterações estruturais de fluxo, escopo ou regra de negócio devem ser registradas explicitamente e avaliadas contra ERD/API/domínio antes de modificar a baseline. Ajustes puramente visuais que não alterem comportamento podem evoluir durante a implementação.

## 14. Próximo passo

Converter a baseline aprovada em `EPIC -> Feature -> User Story -> Task` no Backlog MVP e, na sequência, iniciar a Solution .NET e a Migration 001.
