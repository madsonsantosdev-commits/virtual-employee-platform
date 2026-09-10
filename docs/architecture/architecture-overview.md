# Visão Geral da Arquitetura

## 1. Objetivo

Este documento registra a direção arquitetural inicial do Virtual Employee Platform.

A arquitetura deve permitir validar o produto rapidamente, manter baixo custo operacional e preservar caminhos claros de evolução conforme o número de tenants, conversas, agendamentos e pagamentos crescer.

## 2. Estilo arquitetural

O MVP utilizará **Monólito Modular** em ASP.NET Core.

A aplicação será organizada em módulos de domínio com limites explícitos. A separação lógica deverá permitir que módulos específicos sejam extraídos no futuro somente quando métricas técnicas ou de negócio justificarem essa mudança.

Não adotaremos microsserviços ou Kubernetes prematuramente.

## 3. Multi-Tenancy

A plataforma será Multi-Tenant desde o primeiro dia.

Inicialmente será utilizado um banco PostgreSQL compartilhado, com dados pertencentes aos estabelecimentos identificados e isolados por `TenantId`.

Autorização e isolamento de tenant são requisitos arquiteturais, não apenas filtros de interface.

## 4. Canais

### Cliente final

WhatsApp é o canal principal.

### Assinante

O assinante utilizará:

- PWA mobile-first;
- WhatsApp para comandos administrativos selecionados.

## 5. Camadas macro

A arquitetura está organizada conceitualmente em cinco áreas:

1. **Canais** — WhatsApp do cliente, WhatsApp administrativo e PWA.
2. **Experiência e Orquestração** — Conversation Engine, AI Gateway e Messaging.
3. **Core do Negócio** — Business Profile, Services, Professionals, Customers e Scheduling.
4. **Financeiro e Inteligência** — Payments, Refunds, Billing, Analytics e Usage Metering.
5. **Plataforma e Infraestrutura** — Identity/Tenants, PostgreSQL, workers, observabilidade e segurança.

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

Os limites e contratos desses módulos serão detalhados em documentos posteriores.

## 7. Domínio operacional central

O **Scheduling Engine** é o domínio operacional central do MVP.

Ele deverá conhecer regras necessárias para disponibilidade, serviços, profissionais e appointments, mas não deverá depender diretamente de SDKs do WhatsApp, OpenAI ou gateways de pagamento.

## 8. Inteligência Artificial

A IA não é o centro da arquitetura. É uma capacidade utilizada pela camada de conversação.

Regra arquitetural:

> **IA interpreta → Backend valida → Domínio executa.**

Para operações financeiras:

> **IA interpreta → Usuário autoriza quando necessário → Backend valida → Domínio executa → Gateway processa → Webhook confirma.**

A IA não terá acesso direto ao banco de dados.

O AI Gateway será responsável por abstrair provedores/modelos, contexto, custos, tokens, logs e políticas de uso por tenant.

## 9. Persistência

Inicialmente:

- PostgreSQL como banco transacional principal;
- um banco compartilhado;
- isolamento por `TenantId`;
- Azure Blob Storage para objetos/arquivos quando necessário.

Não haverá banco separado por módulo ou tenant no MVP.

## 10. Processamento assíncrono

Processamento assíncrono será usado onde trouxer benefício operacional, principalmente para:

- lembretes;
- mensagens de saída;
- retries;
- processamento de webhooks;
- estornos;
- tarefas analíticas e de background.

Azure Functions ou workers poderão executar esses jobs.

Azure Service Bus é um ponto de evolução e será introduzido quando houver necessidade concreta de desacoplamento, escala ou confiabilidade adicional.

## 11. Integrações externas

Principais integrações previstas:

- Meta / WhatsApp Business;
- provedor de LLM;
- gateway de pagamentos de serviços;
- provedor de cobrança da assinatura SaaS.

Todas devem ser acessadas através de adapters/abstrações para impedir acoplamento do domínio aos SDKs externos.

## 12. Pagamentos

Existem dois fluxos financeiros independentes.

### Customer Payments

O cliente paga o estabelecimento pelo serviço. O gateway processa o pagamento e a plataforma orquestra e acompanha o estado da transação.

### SaaS Billing

O estabelecimento paga a assinatura da plataforma. Esse fluxo utiliza o Billing Engine e um provedor de cobrança SaaS, com liquidação destinada à conta PJ da plataforma.

A assinatura SaaS não será descontada das vendas do estabelecimento.

## 13. Segurança

Security by Design é requisito transversal.

Inclui:

- autenticação e autorização;
- isolamento por tenant;
- princípio do menor privilégio;
- gerenciamento seguro de secrets;
- checkout hospedado/tokenizado;
- ausência de dados de cartão no WhatsApp, IA, logs e banco da aplicação;
- validação de assinatura/autenticidade de webhooks;
- idempotência;
- proteção contra replay e duplicidade;
- trilha de auditoria;
- rate limiting;
- proteção de dados alinhada à LGPD.

## 14. Observabilidade e custos

Observability by Design deverá incluir logs estruturados, métricas, traces e correlação entre operações.

A plataforma também deverá medir consumo por tenant, especialmente:

- mensagens WhatsApp;
- requisições de IA;
- tokens de entrada e saída;
- custo estimado de IA;
- appointments;
- pagamentos;
- refunds.

## 15. Stack inicial

- Backend: ASP.NET Core
- ORM: Entity Framework Core
- Banco: PostgreSQL
- Frontend: Next.js + React + PWA
- Background: Azure Functions e/ou workers
- Storage: Azure Blob Storage
- Observabilidade: Application Insights
- CI/CD: GitHub Actions ou Azure DevOps, decisão final pendente
- IaC: Terraform após estabilização da infraestrutura

## 16. Princípios arquiteturais

- Monólito Modular primeiro.
- Multi-Tenant desde o primeiro dia.
- API-first.
- WhatsApp-first para o cliente.
- PWA mobile-first para o assinante.
- Limites orientados pelo domínio.
- IA interpreta; backend executa.
- Integrações externas através de adapters.
- Segurança e observabilidade desde a concepção.
- Monitoramento de custos por tenant.
- Processamento assíncrono quando apropriado.
- Sem microsserviços prematuros.
- Sem Kubernetes no MVP.

## 17. Evolução

A arquitetura deverá evoluir baseada em evidências.

Módulos como Messaging, AI, Notifications ou processamento de webhooks financeiros poderão ser extraídos futuramente se volume, isolamento, disponibilidade ou escala justificarem a complexidade operacional adicional.

A próxima etapa arquitetural é definir formalmente os **limites dos módulos, responsabilidades, dependências permitidas e contratos entre eles**.
