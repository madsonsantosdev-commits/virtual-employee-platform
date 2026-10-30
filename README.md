# Virtual Employee Platform

Plataforma SaaS Multi-Tenant para pequenos negócios de serviços, com atendimento WhatsApp-first, agendamento, pagamentos e inteligência artificial.

## Visão

O produto é um **funcionário virtual** que atende clientes pelo WhatsApp, conhece os serviços do estabelecimento, consulta disponibilidade, agenda, reagenda, cancela, conduz o pagamento, envia confirmações e lembretes e ajuda o assinante a administrar o negócio.

## Princípios principais

- WhatsApp-first para o cliente final

- PWA mobile-first para o assinante

- Monólito Modular no MVP

- Multi-Tenant desde o primeiro dia

- IA interpreta; backend valida e executa

- Pagamentos e assinaturas SaaS em fluxos financeiros separados

- Security by Design, Privacy by Design/LGPD e Observability by Design

- Sem adoção prematura de microsserviços

## Documentação

- [Visão do Produto](docs/product/product-vision.md)

- [Visão Geral da Arquitetura](docs/architecture/architecture-overview.md)

- [LGPD & Privacy Architecture v1](docs/architecture/privacy-lgpd-v1.md)

- [Módulos e Limites Arquiteturais](docs/architecture/modules.md)

- [Interações e Eventos](docs/architecture/interactions-and-events.md)

- [Modelo de Domínio v1](docs/domain/domain-model.md)

- [ERD Físico v1](docs/domain/erd-v1.md)

- [Scheduling Domain](docs/domain/scheduling.md)

- [Contratos API v1](docs/api/contracts-v1.md)

- [ADRs](docs/architecture/adr/)

- [Setup de Desenvolvimento Local](docs/development/local-setup.md)

## Diagramas de Arquitetura

Os diagramas oficiais da arquitetura são mantidos no Eraser e complementam a documentação versionada neste repositório.

- [Virtual Employee Platform — Workspace de Arquitetura no Eraser](https://app.eraser.io/workspace/UcBRZVFG8H5U6A3qDZwZ)

O workspace contém, entre outros artefatos, as visões **Macro Architecture** e **High-Level Architecture**. Novos diagramas, como C4 System Context, C4 Container, fluxos de pagamento e modelo de dados, devem permanecer organizados nesse mesmo workspace.

## Status

MVP em implementação por entregas incrementais.

### Fundação técnica

- [x] Solution .NET 10

- [x] Estrutura Domain / Application / Infrastructure / API

- [x] Projetos de testes

- [x] PostgreSQL 18 via Docker

- [x] EF Core + Npgsql

- [x] AppDbContext e migrations

- [x] Persistência de Tenant

- [x] Banco separado para testes de integração

- [x] Testes de integração com PostgreSQL real

- [x] Testes de isolamento de tenant na persistência

- [x] Pipeline de CI configurado

### Negócio e catálogo

- [x] Base de Business e Location

- [x] Base de Services

- [x] Vínculo LocationService

- [x] Entidade Professional e persistência

- [x] Application e serviços de leitura e escrita de Professionals

- [x] API de Professionals: criação, listagem, consulta por ID e atualização

- [x] Validação e isolamento entre tenants em Professionals

- [x] Testes automatizados dos endpoints de Professionals

- [x] Vínculo ProfessionalLocation: persistência, leitura e substituição via API

- [x] Validação de Business e isolamento entre tenants em ProfessionalLocations

- [x] Vínculo ProfessionalService e regras de elegibilidade

- [x] Customers: persistência, criação, listagem e consulta por ID

- [x] Normalização de telefone e isolamento entre tenants em Customers

### Identidade e acesso

- [x] TenantContext e filtros de consulta por tenant

- [x] Proteções de escrita e referências entre tenants

- [ ] Autenticação e resolução de tenant pela identidade autenticada

- [ ] Cadastro e verificação de conta

- [ ] Papéis e permissões de usuários internos

O header `X-Tenant-Id` é um mecanismo temporário de desenvolvimento.

A autenticação e a autorização de produção ainda estão pendentes.

### Próximas capacidades do MVP

- [x] AvailabilityRule: persistência, leitura e substituição via API

- [x] Validações de janelas, recursos ativos, Business e isolamento entre tenants

- [x] Busca de slots por agenda semanal, elegibilidade, duração e timezone

- [x] ScheduleBlocks: criação idempotente, consulta e desativação

- [x] Descontar bloqueios da unidade e do profissional na busca de slots

- [ ] Avaliação de impacto dos bloqueios sobre Appointments

- [x] Descontar agendamentos ocupantes na busca de slots

- [x] Booking Quote: cálculo de preço e duração no backend

- [x] Appointments: criação idempotente, snapshots e consulta por ID

- [x] Proteção contra agendamentos concorrentes entre unidades

- [x] Expiração de reservas PENDING com worker configurável

- [x] Listagem paginada de Appointments com filtros e isolamento por tenant

- [x] Cancelamento idempotente de Appointments com motivo e auditoria

- [x] Liberação da agenda após cancelamento persistido

- [x] Reagendamento idempotente de Appointments com preservação dos snapshots

- [x] Histórico de criação, cancelamento, reagendamento e expiração de Appointments

- [x] Consulta paginada do histórico com isolamento por tenant

- [ ] Publicação de eventos de domínio/integração

- [x] Confirmação de Appointments PENDING por pagamento integral, com reserva vigente

- [x] Domínio e persistência de Payment e PaymentAttempt

- [x] Criação idempotente de tentativas com recuperação após resultado incerto

- [x] Endpoint de PaymentAttempts habilitado por configuração e validado com gateway simulado

- [x] Adaptador Mercado Pago Checkout Pro via Orders API, validado com contas de teste

- [x] Webhook de Orders autenticado e confirmação financeira idempotente
- [ ] Reconciliação automática e tratamento completo dos eventos financeiros

- [ ] Refunds e compensação financeira

- [ ] Integração WhatsApp

- [ ] Conversation Engine e AI Gateway

- [ ] PWA

- [ ] Dashboard e Analytics

- [ ] SaaS Billing

### Última validação local — 30/10/2026

- Suíte completa em Release: 1738 testes passaram, sem falhas ou ignorados.
- Criação real de checkout no Mercado Pago validada com HTTP 201 e replay com corpo idêntico.
- Ordem aprovada consultada pela API do provedor, com valor integral de R$ 65,00 e moeda BRL.
- Webhook do simulador do Mercado Pago recebido por túnel HTTPS e processado com HTTP 200.
- Assinatura HMAC-SHA256 validada com ID original ou normalizado para minúsculas; comparação em tempo constante.
- Ordem e pagamento consultados com autenticação antes da confirmação; valores do corpo da notificação não são usados como prova financeira.
- Validados recebedor, aplicação, referência da tentativa, valor integral, moeda, aprovação e ausência de estorno.
- Validação manual de reserva vencida: Payment e PaymentAttempt CONFIRMED; Appointment preservado como EXPIRED.
- Resultado persistido com RequiresCompensation=true e data de aprovação original do provedor.
- Reenvio preservou um único registro de confirmação, as contagens do histórico e confirmed_at.
- Confirmação com reserva vigente e demais regras operacionais cobertas por testes automatizados; validação manual desse cenário permanece pendente.
- Factories de Appointments isoladas das flags locais de Payments, Mercado Pago, webhooks e túnel.
- Autenticação de produção, OAuth, reconciliação automática e execução de refunds permanecem pendentes.

Professionals, ProfessionalLocations, ProfessionalServices, os filtros
de elegibilidade, AvailabilityRules, busca de slots, ScheduleBlocks,

Customers, Booking Quote, criação, listagem, cancelamento e reagendamento
de Appointments estão integrados à `main`.

A organização dos testes em regiões Facts, Theories e Helpers também
está integrada.

O histórico de Appointments está integrado à `main`.

A base de Payments, a orquestração de tentativas e o endpoint HTTP estão
integrados à `main`.

O adaptador Mercado Pago, a verificação financeira, o webhook e a confirmação
por pagamento estão implementados na branch `feature/mercado-pago`.
A integração dessa entrega à `main` permanece pendente.

A rota de tentativas exige `Payments:Enabled=true` e uma implementação
de IPaymentGateway registrada. Nenhum gateway fictício é registrado
na execução normal da API.

A criação revalida recursos e disponibilidade, calcula os valores no
backend e persiste Appointment, itens, histórico e resposta idempotente
na mesma transação.

As consultas retornam o estado atual; o replay de criação preserva
a resposta original e não reativa reservas expiradas ou canceladas.

A listagem possui filtros opcionais, paginação e ordenação por início
e ID. Itens e contagem respeitam o tenant atual.

Consultar agendamentos não altera seu estado nem materializa expiração.

O cancelamento grava estado, motivo, data, histórico e resposta idempotente
na mesma transação. O intervalo é liberado quando a transação confirma.

Itens e snapshots comerciais são preservados.

O replay de cancelamento devolve a resposta original.

O reagendamento permite alterar horário, unidade e profissional dentro
do mesmo Business, após revalidar recursos e disponibilidade.

Preserva AppointmentId, Customer, itens, snapshots e estado operacional.

A duração persistida determina o novo término, sem recalcular os valores
comerciais pelo catálogo atual.

A alteração, o histórico e a resposta idempotente são persistidos
na mesma transação. O commit libera o intervalo anterior e ocupa o novo.

O replay devolve a resposta original, sem reaplicar a mudança.

Reservas PENDING exigem prazo vigente. O reagendamento não estende
a reserva e reduz seu prazo quando o novo início antecede a expiração.

Tentativas de cancelar ou reagendar PENDING vencido materializam EXPIRED
e seu histórico e retornam conflito, sem registrar sucesso idempotente
da operação solicitada.

A busca de slots considera agenda semanal, elegibilidade, bloqueios
e agendamentos nos estados que ocupam agenda, inclusive em outras
unidades do mesmo profissional dentro do tenant.

O prazo padrão da reserva é de 10 minutos, limitado ao início do atendimento.

O worker de expiração fica desabilitado por padrão e exige ativação explícita.

Quando habilitado, o intervalo padrão entre ciclos é de 30 segundos.

Reservas PENDING continuam ocupando agenda até a expiração ou
o cancelamento serem persistidos.

A expiração em lote atualiza reservas e insere seus históricos atomicamente.

O histórico registra criação, cancelamento, reagendamento, expiração e
confirmação operacional por pagamento (PAYMENT_CONFIRMED),
com estados anterior e novo e motivo quando houver cancelamento.

Replay e operações sem mudança efetiva não acrescentam registros.

Agendamentos existentes não recebem registros retroativos.

A consulta do histórico é paginada, ordenada por data e ID e isolada
por tenant. Não altera o agendamento nem materializa expiração.

SaveChanges rejeita alterações e exclusões rastreadas do histórico;
essa proteção não abrange SQL direto nem operações em lote.

### Mercado Pago: configuração e limites atuais

O adaptador exige `Payments:Enabled=true` e `Payments:MercadoPago:Enabled=true`.
O webhook também exige `Payments:MercadoPago:WebhooksEnabled=true`;
permanece desabilitado por padrão.

Cada conta configurada vincula TenantId, BusinessId, AccessToken, CollectorId,
ApplicationId, WebhookId e WebhookSecret. Credenciais ficam fora do repositório.
WebhookSecret é o segredo de assinatura; não é o Access Token.

A rota `POST /webhooks/mercado-pago/{webhookId}` não usa X-Tenant-Id.
WebhookId localiza a conta, e a assinatura autentica a requisição antes da
inicialização do tenant. O ID assinado vem de `data.id` na query string.
A API consulta a ordem e usa `transactions.payments[].reference_id` para
consultar o pagamento numérico. IDs `ORD...`, `PAY...` e o ID numérico do
pagamento representam identificadores distintos.

A confirmação e o resultado idempotente são persistidos atomicamente.
A deduplicação considera tenant, provedor e ID financeiro do pagamento;
não há inbox durável separado por ID de notificação.
O sucesso HTTP é enviado após a persistência ou o replay do resultado.
O prazo local de processamento é de 18 segundos. Falhas transitórias retornam
503. A verificação ainda agrupa estados não aprovados e respostas incompatíveis
em um resultado sem confirmação, também tratado com 503 pela rota.

O fluxo atual aceita cartão de crédito ou débito e uma única transação por ordem.
PIX e ordens com múltiplas transações ainda exigem implementação e validação.
Uma notificação anterior à associação local da ordem retorna 503 para nova tentativa.
Não há reconciliação automática para entregas que deixem de ser reenviadas.

O prazo operacional é verificado no processamento local. Pagamento aprovado
antes do vencimento, mas processado após o prazo, não reativa a reserva.
RequiresCompensation registra a necessidade de tratamento financeiro;
não cria nem executa um estorno automaticamente.

A validação manual usou contas de teste e o simulador do painel. O pagamento
consultado retornou live_mode=true, apesar da identificação como teste no painel;
essa divergência precisa ser esclarecida antes da liberação em produção.

Para túnel temporário, `WebhookTunnel:Enabled=true` permite somente POSTs
sob `/webhooks/mercado-pago` e bloqueia as demais rotas, inclusive Swagger e API.
Mantenha esse modo ativo durante a exposição pública e encerre o túnel ao terminar.
A URL temporária muda quando o túnel é recriado.

Permanecem pendentes publicação de eventos, OAuth por estabelecimento,
reconciliação automática e refunds, incluindo execução de compensações por
pagamentos tardios ou excedentes.

O cancelamento atual não solicita nem confirma estorno.

A avaliação de impacto dos bloqueios sobre Appointments e o fluxo
de reagendamento assistido também permanecem pendentes.
