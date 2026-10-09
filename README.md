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
- [ ] Busca de slots disponíveis
- [ ] Appointment e proteção contra agendamentos concorrentes
- [ ] Payments e Refunds
- [ ] Integração WhatsApp
- [ ] Conversation Engine e AI Gateway
- [ ] PWA
- [ ] Dashboard e Analytics
- [ ] SaaS Billing

### Última validação local — 09/10/2026

- Suíte completa: 328 testes passaram, sem falhas ou ignorados.
- AvailabilityRules: 16 testes de domínio, 16 de Application, 14 de persistência e 21 HTTP.
- Validados horários, sobreposição, recursos inexistentes ou inativos, conflito de Business e isolamento entre tenants.
- Validação manual no PowerShell: leitura, substituição, rejeição de sobreposição, limpeza e reativação da agenda.
- IDs preservados na desativação e reativação das mesmas janelas.

Professionals, ProfessionalLocations, ProfessionalServices e os filtros
de elegibilidade estão integrados à `main`.

AvailabilityRules está implementado na branch `feature/availability-rules`,
com validação automatizada e manual concluída e integração à `main` pendente.

A próxima etapa prevista é implementar a busca de slots disponíveis,
considerando as regras de disponibilidade e a elegibilidade dos profissionais.
