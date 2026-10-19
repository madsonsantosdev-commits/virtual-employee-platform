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
- [ ] Reagendamento de Appointments
- [ ] Confirmação de Appointments por pagamento integral
- [ ] Payments e Refunds
- [ ] Integração WhatsApp
- [ ] Conversation Engine e AI Gateway
- [ ] PWA
- [ ] Dashboard e Analytics
- [ ] SaaS Billing

### Última validação local — 19/10/2026

- Suíte completa: 893 testes passaram, sem falhas ou ignorados.
- Cancelamento: 25 testes de domínio, 24 do handler, 20 de persistência/serviço e 23 HTTP.
- Validadas origens CLIENT e BUSINESS, motivo obrigatório e limite de 500 caracteres após Trim.
- Validados auditoria em UTC, preservação dos snapshots e liberação do intervalo para nova reserva.
- Idempotência validada com replay, normalização do motivo e conflito de conteúdo.
- Concorrência validada com mesma chave, chaves diferentes e disputa com expiração.
- Validados isolamento entre tenants, integridade no PostgreSQL e expiração no limite do prazo.
- Validação manual: cancelamento 200, replay 200 com mesmo corpo e consulta com CANCELLED_BY_CLIENT.
- Motivo com acentos preservado; preço de 65,00 e duração de 45 minutos mantidos.

Professionals, ProfessionalLocations, ProfessionalServices, os filtros
de elegibilidade, AvailabilityRules, busca de slots, ScheduleBlocks,
Customers, Booking Quote, criação e listagem de Appointments estão
integrados à `main`.
A organização dos testes em regiões Facts, Theories e Helpers também
está integrada.

O cancelamento de Appointments está implementado na branch
`feature/appointments-cancel`, com validação automatizada e manual
concluída e integração à `main` pendente.

A criação revalida recursos e disponibilidade, calcula os valores no
backend e persiste Appointment, itens e resposta idempotente atomicamente.
As consultas retornam o estado atual; o replay de criação preserva
a resposta original e não reativa reservas expiradas ou canceladas.

A listagem possui filtros opcionais, paginação e ordenação por início
e ID. Itens e contagem respeitam o tenant atual.
Consultar agendamentos não altera seu estado nem materializa expiração.

O cancelamento grava estado, motivo, data e resposta idempotente
na mesma transação. O intervalo é liberado quando a transação confirma.
Itens e snapshots comerciais são preservados.
O replay de cancelamento devolve a resposta original.

A tentativa de cancelar uma reserva PENDING vencida materializa EXPIRED
e retorna conflito, sem registrar cancelamento.
A disputa entre cancelamento e expiração respeita o token de concorrência.

A busca de slots considera agenda semanal, elegibilidade, bloqueios
e agendamentos nos estados que ocupam agenda, inclusive em outras
unidades do mesmo profissional dentro do tenant.

O prazo padrão da reserva é de 10 minutos, limitado ao início do atendimento.
O worker de expiração fica desabilitado por padrão e exige ativação explícita.
Quando habilitado, o intervalo padrão entre ciclos é de 30 segundos.
Reservas PENDING continuam ocupando agenda até a expiração ou
o cancelamento serem persistidos.

Permanecem pendentes reagendamento, histórico de transições e integração
com pagamentos, incluindo confirmação por pagamento integral e refunds.
O cancelamento atual não solicita nem confirma estorno.
A avaliação de impacto dos bloqueios sobre Appointments também permanece
pendente.