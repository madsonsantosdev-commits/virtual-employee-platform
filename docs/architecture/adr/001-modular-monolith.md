# ADR-001 — Monólito Modular como arquitetura inicial

- **Status:** Accepted
- **Data:** 2026-09-10

## Contexto

O produto é um SaaS Multi-Tenant em fase de MVP. Precisamos equilibrar velocidade de entrega, baixo custo operacional, simplicidade de deploy e disciplina arquitetural suficiente para permitir evolução futura.

Adotar microsserviços desde o início aumentaria complexidade de deploy, observabilidade, transações distribuídas, mensageria, troubleshooting e custo de infraestrutura antes de existir evidência de escala que justifique isso.

## Decisão

O backend será construído inicialmente como um **Monólito Modular em ASP.NET Core**.

Os módulos terão:

- responsabilidades explícitas;
- contratos próprios;
- acesso restrito aos dados de outros módulos;
- dependências controladas;
- possibilidade de comunicação por chamadas internas e eventos;
- adapters para integrações externas.

A unidade principal de deploy do MVP será única.

## Consequências positivas

- menor complexidade operacional;
- deploy simplificado;
- transações locais mais simples;
- menor custo de infraestrutura;
- debugging mais direto;
- maior velocidade para validar o produto;
- possibilidade de manter limites de domínio desde o início.

## Consequências negativas

- exige disciplina para evitar acoplamento entre módulos;
- falhas de isolamento físico não existem como em serviços separados;
- escala inicialmente ocorre no conjunto da aplicação;
- extração futura de módulos pode exigir refatoração.

## Regras derivadas

- nenhum módulo acessa diretamente tabelas internas de outro;
- dependências cíclicas são proibidas;
- SDKs externos permanecem em adapters;
- Shared Kernel deve ser mínimo;
- extração para microsserviço só acontece com evidência concreta.

## Critérios de evolução

Um módulo poderá ser extraído quando houver necessidade comprovada de:

- escala independente;
- isolamento de falhas;
- deploy independente;
- requisitos de segurança específicos;
- equipe responsável independente;
- volume operacional muito diferente dos demais módulos.

Possíveis candidatos futuros incluem Messaging, AI Gateway, Notifications e processamento de webhooks financeiros.
