# Visão Geral da Arquitetura

## 1. Objetivo

Este documento registra a direção arquitetural inicial do Virtual Employee Platform.

A arquitetura deve permitir validar o produto rapidamente, manter baixo custo operacional e preservar caminhos claros de evolução conforme o número de tenants, locations, conversas, agendamentos e pagamentos crescer.

## 2. Estilo arquitetural

O MVP utilizará **Monólito Modular** em ASP.NET Core. A aplicação será organizada em módulos de domínio com limites explícitos. Não adotaremos microsserviços ou Kubernetes prematuramente.

## 3. Multi-Tenancy e estrutura organizacional

A plataforma será Multi-Tenant desde o primeiro dia, usando inicialmente PostgreSQL compartilhado e isolamento por `TenantId`.

Fronteiras aprovadas:

```text
Tenant = propriedade, segurança e cobrança
Business = negócio/marca
Location = unidade operacional física ou virtual
LegalEntity = identidade jurídica/fiscal
```

No MVP a experiência inicial será 1 Tenant -> 1 Business -> 1 Location, mas o modelo suporta múltiplas Locations. Autorização e isolamento são requisitos arquiteturais, não filtros de interface.

### Estratégia de integridade com foco em performance

`TenantId` é a principal barreira estrutural no banco para dados tenant-owned. Relacionamentos críticos usam validação tenant-aware e índices tenant-scoped.

Regras específicas de coerência operacional de Business, Location, Service e Professional permanecem prioritariamente no domínio/aplicação, evitando inflar todas as FKs com chaves compostas maiores quando isso não entrega benefício proporcional.

Princípio aprovado:

> **Integridade suficiente para impedir vazamento entre tenants, com domínio responsável por regras específicas de Business e índices/constraints adicionais orientados por hot paths reais e pelo equilíbrio entre integridade e performance.**

Isso não reduz a segurança: a aplicação valida contexto/autorização, o domínio valida invariantes operacionais e o PostgreSQL continua como última barreira para isolamento crítico.

## 4. Canais

Cliente final: WhatsApp. Assinante: PWA mobile-first + WhatsApp para comandos administrativos selecionados.

## 5. Camadas macro

1. **Canais** — WhatsApp do cliente, WhatsApp administrativo e PWA.
2. **Experiência e Orquestração** — Conversation Engine, AI Gateway e Messaging.
3. **Core do Negócio** — Business Profile, Locations, Services, Professionals, Customers e Scheduling.
4. **Financeiro e Inteligência** — Payments, PaymentAttempts, Refunds, Billing, Analytics e Usage Metering.
5. **Plataforma e Infraestrutura** — Identity/Tenants, PostgreSQL, workers, observabilidade, segurança e privacidade.

Provedores externos permanecem atrás de adapters.

## 6. Módulos iniciais

- Identity & Tenants
- Business Profile
- Services
- Professionals
- Customers
- Scheduling
- Conversation
- AI Gateway
- Messaging
- Payments
- Refunds
- Analytics
- Billing
- Usage Metering
- Audit & Observability

Privacy/LGPD é requisito transversal, não um módulo isolado que possa ser ignorado pelos demais.

## 7. Domínio operacional central

O **Scheduling Engine** é o domínio operacional central do MVP. Conhece as regras necessárias para disponibilidade, serviços, profissionais, locations e appointments, mas não depende diretamente de SDKs do WhatsApp, LLM ou gateways de pagamento.

Os hot paths de performance serão medidos e otimizados principalmente em:

```text
Tenant + Location
-> LocationService
-> ProfessionalLocation
-> ProfessionalService
-> AvailabilityRule
-> ScheduleBlock
-> Appointments ativos
-> Slots disponíveis
```

Índices serão criados a partir desses padrões de acesso e de evidência real de uso, evitando índices redundantes que aumentem custo de escrita e memória.

## 8. Inteligência Artificial

A IA não é o centro da arquitetura. É uma capacidade da camada de conversação.

> **IA interpreta → Backend valida → Domínio executa.**

Para operações financeiras:

> **IA interpreta → Usuário autoriza quando necessário → Backend valida → Domínio executa → Gateway processa → Webhook confirma.**

A IA não terá acesso direto ao banco. O AI Gateway abstrai provedores/modelos, contexto, custos, tokens, logs e políticas por tenant.

Privacidade na IA:

```text
Canal
 -> Conversation Engine
 -> Context Builder / Data Minimization
 -> AI Gateway
 -> LLM Provider
```

Somente dados necessários à tarefa são enviados ao modelo. CPF/CNPJ, dados de cartão, secrets e dados financeiros sem relação com a tarefa não entram no contexto por padrão.

## 9. Persistência

- PostgreSQL transacional principal;
- banco compartilhado;
- isolamento por `TenantId`;
- índices tenant-scoped orientados por consultas reais;
- coerência específica de Business validada no domínio;
- constraints adicionais somente quando houver ganho claro de integridade/concorrência;
- Azure Blob Storage quando necessário;
- sem banco separado por módulo/tenant no MVP.

Não será adotada uma estratégia de adicionar `BusinessId` a todas as FKs compostas indiscriminadamente. O desenho busca reduzir largura de índices, custo de escrita e complexidade no EF Core sem enfraquecer o isolamento entre tenants.

## 10. Processamento assíncrono

Usado para lembretes, mensagens de saída, retries, webhooks, refunds e tarefas analíticas/background. Azure Functions ou workers poderão executar jobs. Azure Service Bus é ponto de evolução quando houver necessidade concreta.

## 11. Integrações externas

Previstas: Meta/WhatsApp Business, provedor de LLM, gateway de pagamentos de serviços e provedor de billing SaaS. Todas ficam atrás de adapters.

Integrações que tratam dados pessoais devem constar no inventário de provedores/suboperadores, com finalidade e categorias de dados compartilhadas.

## 12. Pagamentos

Existem dois fluxos financeiros independentes.

### Customer Payments
Cliente paga o estabelecimento. Gateway processa e a plataforma orquestra/acompanha. Dados brutos de cartão não trafegam pela aplicação; usar checkout hospedado/tokenizado.

O modelo separa:

```text
Appointment
  -> Payment              = obrigação financeira lógica
       -> PaymentAttempt  = tentativa concreta no gateway
```

Um Payment pode receber várias tentativas sem criar novas obrigações financeiras. Webhook confiável confirma o PaymentAttempt; então o Payment é confirmado e somente depois o Appointment pode ser confirmado financeiramente.

### SaaS Billing
Estabelecimento paga a assinatura da plataforma. Billing Engine + provedor de cobrança, com liquidação para a conta PJ da plataforma. Mensalidade não é descontada das vendas do estabelecimento.

Subscription possui unidades faturáveis explícitas (`SubscriptionUnit -> Location`), evitando assumir que uma assinatura cobre todas as unidades.

## 13. Security by Design

Inclui autenticação/autorização, isolamento por tenant, menor privilégio, secret management, checkout hospedado/tokenizado, ausência de cartão em WhatsApp/IA/logs/DB, validação de webhooks, idempotência, proteção contra replay/duplicidade, auditoria e rate limiting.

## 14. Privacy by Design / LGPD

Privacy by Design e Privacy by Default são requisitos arquiteturais transversais.

Diretrizes:
- finalidade definida para cada categoria de dado;
- minimização/necessidade;
- transparência;
- acesso restrito;
- retenção definida;
- exclusão ou anonimização quando cabível;
- capacidade técnica de atender direitos do titular;
- papéis Controlador/Operador definidos por finalidade/fluxo;
- compartilhamento mínimo com provedores;
- PII redigida/mascarada em logs e interfaces quando possível;
- incidentes de segurança com processo documentado.

Documento normativo do projeto: `docs/architecture/privacy-lgpd-v1.md`.

## 15. Observabilidade e custos

Observability by Design inclui logs estruturados, métricas, traces e correlação. Logs devem privilegiar IDs técnicos e evitar PII desnecessária.

Medir por tenant e, quando útil, por Location: WhatsApp, IA/tokens/custos, appointments, payments, payment attempts e refunds.

## 16. Stack inicial

- Backend: ASP.NET Core
- ORM: Entity Framework Core
- Banco: PostgreSQL
- Frontend: Next.js + React + PWA
- Background: Azure Functions e/ou workers
- Storage: Azure Blob Storage
- Observabilidade: Application Insights
- CI/CD: GitHub Actions ou Azure DevOps, decisão final pendente
- IaC: Terraform após estabilização da infraestrutura

## 17. Princípios arquiteturais

- Monólito Modular primeiro.
- Multi-Tenant desde o primeiro dia.
- Tenant != Location.
- `TenantId` como principal barreira estrutural de isolamento no banco.
- Regras específicas de Business validadas no domínio.
- Índices orientados por hot paths reais.
- Evitar constraints e índices redundantes.
- Equilíbrio entre integridade, simplicidade e performance.
- API-first.
- WhatsApp-first para cliente.
- PWA mobile-first para assinante.
- Limites orientados pelo domínio.
- IA interpreta; backend executa.
- Integrações por adapters.
- Security by Design.
- **Privacy by Design / LGPD.**
- Observability by Design.
- Monitoramento de custos por tenant.
- Processamento assíncrono quando apropriado.
- Sem microsserviços prematuros.
- Sem Kubernetes no MVP.

## 18. Evolução

A arquitetura evolui baseada em evidências. Messaging, AI, Notifications ou processamento de webhooks financeiros poderão ser extraídos futuramente se volume, isolamento, disponibilidade ou escala justificarem.

Antes da implementação, além do fechamento do ERD/API, deve existir uma baseline de privacidade: inventário de dados, finalidades/bases legais candidatas, papéis Controlador/Operador, retenção, processo de direitos do titular, fornecedores/suboperadores e procedimento de incidentes.
